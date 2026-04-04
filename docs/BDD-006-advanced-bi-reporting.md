# BDD-006 — Advanced Business Intelligence & Reporting
**Fase:** 17 | **Estado:** Diseño | **Fecha:** 2026-04-03

---

## 1. Contexto y Diagnóstico del Estado Actual

### Problema de rendimiento identificado

El `ReportService` actual carga grafos completos de entidades en memoria usando `GetAsync(filter, "Items")`:

```
_unitOfWork.Orders.GetAsync(
    o => o.BranchId == branchId && ...,
    "Items"   // ← EF carga TODOS los OrderItems y sus relaciones
)
```

Para un negocio con 10,000+ órdenes esto materializa objetos innecesarios, sin `AsNoTracking`, con todos los campos de cada entidad. Los nuevos endpoints BI **no pueden** seguir este patrón.

### Brecha de funcionalidad

| Necesidad del negocio | Estado actual |
|---|---|
| Gráfica de ventas por hora / día / mes | ❌ No existe |
| Top productos con revenue exacto | ⚠️ Existe solo en `GetSummaryAsync` (en memoria) |
| Desglose por método de pago y proveedor | ⚠️ Solo Cash/Card hardcoded |
| CSV detallado con cliente y estado fiscal | ⚠️ `GenerateFiscalCsvAsync` no incluye Customer |

---

## 2. Nuevos DTOs — `ReportModels.cs`

### 2.1 `SalesPointDto`
Representa un punto en la gráfica de ventas a lo largo del tiempo.

| Propiedad | Tipo | Descripción |
|---|---|---|
| `Date` | `DateTime` | Marca temporal del período (hora/día/mes según granularidad) |
| `TotalCents` | `int` | Suma de `TotalCents` de órdenes pagadas |
| `OrderCount` | `int` | Número de órdenes completadas en el período |

### 2.2 `TopProductDto`
Producto más vendido en el período.

| Propiedad | Tipo | Descripción |
|---|---|---|
| `ProductName` | `string` | Nombre del producto (`OrderItem.ProductName`) |
| `QuantitySold` | `int` | Suma de `OrderItem.Quantity` |
| `TotalRevenueCents` | `int` | Suma de `Quantity * UnitPriceCents` |

> **Nota de naming:** Se usa `TotalRevenueCents` en lugar del genérico `TotalCents` para mayor claridad en contexto BI.

### 2.3 `PaymentMethodSalesDto`
Ventas agrupadas por método de pago **y** proveedor externo.

| Propiedad | Tipo | Descripción |
|---|---|---|
| `PaymentMethod` | `string` | Valor del enum `PaymentMethod` como string ("Cash", "Card", etc.) |
| `Provider` | `string?` | `OrderPayment.PaymentProvider` — "Clip", "MercadoPago" o `null` |
| `TotalCents` | `int` | Suma de `OrderPayment.AmountCents` del grupo |
| `TransactionCount` | `int` | Número de transacciones del grupo |

### 2.4 `DashboardChartsDto`
DTO raíz que el controlador devuelve como respuesta.

| Propiedad | Tipo | Descripción |
|---|---|---|
| `SalesOverTime` | `List<SalesPointDto>` | Serie temporal de ventas |
| `TopProducts` | `List<TopProductDto>` | Top 10 productos por cantidad vendida |
| `SalesByPaymentMethod` | `List<PaymentMethodSalesDto>` | Desglose por método/proveedor |

---

## 3. Actualización de `IReportService`

### 3.1 `GetDashboardChartsAsync`

```
Task<DashboardChartsDto> GetDashboardChartsAsync(
    int branchId,
    DateTime from,
    DateTime to,
    string granularity)
```

**Parámetro `granularity`:**

| Valor | Agrupación de `SalesOverTime` | Key de `SalesPointDto.Date` |
|---|---|---|
| `"hour"` | Por `CreatedAt.Date + Hour` | `new DateTime(year, month, day, hour, 0, 0)` |
| `"day"` | Por `CreatedAt.Date` | `CreatedAt.Date` |
| `"month"` | Por `Year + Month` | `new DateTime(year, month, 1)` |

Si `granularity` no es ninguno de los tres valores válidos → lanzar `ArgumentException`.

**Filtro de órdenes aplicable a los tres componentes del DTO:**
- `BranchId == branchId`
- `CreatedAt.Date >= from.Date && CreatedAt.Date <= to.Date`
- `IsPaid == true`
- `CancellationReason == null`

### 3.2 `GetDetailedSalesCsvAsync`

```
Task<string> GetDetailedSalesCsvAsync(
    int branchId,
    DateTime from,
    DateTime to)
```

**Retorna:** `string` (CSV UTF-8 con BOM).

**Columnas del CSV:**

| Columna | Fuente |
|---|---|
| `OrderId` | `Order.Id` |
| `Date` | `Order.CreatedAt` formateado `yyyy-MM-dd HH:mm:ss` |
| `Total` | `Order.TotalCents / 100m` en `F2` |
| `PaymentMethods` | `string.Join("\|", Order.Payments.Select(p => p.Method))` |
| `CustomerName` | `Order.Customer.FullName` o `""` si es null |
| `Facturado` | `Order.InvoiceStatus == InvoiceStatus.Issued ? "Sí" : "No"` |

> **Cambio vs `GenerateFiscalCsvAsync` existente:** este CSV incluye `CustomerName` (join con `Customer`) y es el reemplazo enriquecido para exportación operativa.

---

## 4. Estrategia de Performance — REGLA CRÍTICA

### 4.1 El problema con el patrón actual

`IGenericRepository<T>.GetAsync()` no expone `IQueryable<T>`, por lo que no permite encadenar `.AsNoTracking()` ni `.Select()` antes de la materialización. Esto fuerza a cargar grafos completos.

### 4.2 Solución arquitectónica para métodos BI

Los dos nuevos métodos BI en `ReportService` **inyectarán `ApplicationDbContext` directamente** (además del `IUnitOfWork` existente), siguiendo el principio de que operaciones de lectura analítica no requieren el patrón Unit of Work (no hay escritura).

```csharp
// Constructor actualizado en ReportService
public ReportService(IUnitOfWork unitOfWork, ApplicationDbContext context)
{
    _unitOfWork = unitOfWork;    // métodos existentes (sin cambio)
    _context = context;          // solo para los nuevos métodos BI
}
```

> Este patrón es compatible con la inyección de dependencias y evita romper los 4 métodos ya existentes.

### 4.3 Plantilla de consulta obligatoria

Todas las queries BI seguirán esta estructura sin excepción:

```csharp
// ✅ CORRECTO — proyección antes de materializar
var data = await _context.Orders
    .AsNoTracking()
    .Where(o => o.BranchId == branchId
             && o.IsPaid
             && o.CancellationReason == null
             && o.CreatedAt.Date >= from.Date
             && o.CreatedAt.Date <= to.Date)
    .Select(o => new
    {
        o.CreatedAt,
        o.TotalCents,
        // Solo los campos necesarios — nunca o.Items, o.Payments completo
    })
    .ToListAsync();

// ❌ PROHIBIDO en métodos BI
var orders = await _unitOfWork.Orders.GetAsync(
    filter, "Items,Payments,Customer");   // Carga grafo completo
```

### 4.4 Joins eficientes para `GetDetailedSalesCsvAsync`

```
Orders
  └─ .SelectMany(Payments)   → solo: Method, PaymentProvider, AmountCents
  └─ .Customer               → solo: FullName (nullable)
```

La proyección aplanará estos joins en un objeto anónimo antes de `ToListAsync()`, evitando el problema N+1.

---

## 5. Controlador — Nuevos Endpoints en `ReportController`

### 5.1 `GET /api/report/dashboard-charts`

| Aspecto | Detalle |
|---|---|
| Método HTTP | `GET` |
| Ruta | `dashboard-charts` |
| Query params | `from` (DateTime), `to` (DateTime), `granularity` (string, default `"day"`) |
| Auth | `[Authorize(Roles = "Owner")]` |
| `branchId` | Extraído de `BranchId` (propiedad de `BaseApiController`) |
| Response 200 | `DashboardChartsDto` |
| Response 400 | `granularity` inválida o parámetros de fecha incorrectos |

### 5.2 `GET /api/report/export/detailed-sales-csv`

| Aspecto | Detalle |
|---|---|
| Método HTTP | `GET` |
| Ruta | `export/detailed-sales-csv` |
| Query params | `from` (DateTime), `to` (DateTime) |
| Auth | `[Authorize(Roles = "Owner")]` |
| `branchId` | Extraído de `BranchId` (propiedad de `BaseApiController`) |
| Response 200 | `FileContentResult` con `"text/csv"`, nombre `ventas-detalladas-{from}-{to}.csv` |
| Response 400 | Error genérico |

---

## 6. Archivos a Modificar / Crear

| Archivo | Acción | Descripción |
|---|---|---|
| `POS.Domain/Models/ReportModels.cs` | **Modificar** | Agregar `SalesPointDto`, `TopProductDto`, `PaymentMethodSalesDto`, `DashboardChartsDto` |
| `POS.Services/IService/IReportService.cs` | **Modificar** | Agregar `GetDashboardChartsAsync` y `GetDetailedSalesCsvAsync` |
| `POS.Services/Service/ReportService.cs` | **Modificar** | Implementar ambos métodos; inyectar `ApplicationDbContext` |
| `POS.API/Controllers/ReportController.cs` | **Modificar** | Agregar 2 endpoints |

> **Sin migraciones** — solo son DTOs de respuesta y lógica de servicio, sin nuevas entidades de base de datos.

---

## 7. Dependencias y Riesgos

| Item | Detalle |
|---|---|
| `Customer.FullName` | Verificar que el modelo `Customer` expone esta propiedad (CRM Phase 16) |
| `InvoiceStatus.Issued` | Valor del enum utilizado para la columna "Facturado" del CSV |
| `ApplicationDbContext` en DI | Ya registrado en `Program.cs`; la inyección en `ReportService` es directa |
| Ordenamiento del CSV | El CSV se ordenará `ORDER BY CreatedAt DESC` para consistencia con los otros exports |
