# BDD-008 — Advanced Split-Printing
## Fase 19: Impresión por Destino — Cocina, Barra y Meseros

**Fecha:** 2026-04-03
**Estado:** Diseño — Pendiente de aprobación
**Dependencias:** BDD-001 (Products / Categories), BDD-003 (Orders / Sync Engine)

---

## 1. Objetivo

Al finalizar la Fase 19, el sistema podrá:

- Clasificar cada `Product` con un **destino de impresión** (`PrintingDestination`): Cocina, Barra o Meseros.
- Al confirmar una orden, generar automáticamente un **`PrintJob` por destino** que contenga únicamente los ítems que corresponden a esa área.
- Rastrear el estado de cada trabajo de impresión (`Pending → Printed | Failed`) con timestamp y conteo de intentos.
- Exponer endpoints REST para que la UI (impresoras, KDS, tablets de cocina) consulte y marque trabajos como impresos.

---

## 2. Auditoría del Estado Actual

### 2.1 Lo que ya existe y es relevante

| Artefacto | Ubicación | Relevancia |
|-----------|-----------|------------|
| `Product` | `POS.Domain/Models/Product.cs` | Recibe el nuevo campo `PrintingDestination` |
| `OrderItem` | `POS.Domain/Models/OrderItem.cs` | Tiene `virtual Product? Product` — navegación disponible |
| `Order` | `POS.Domain/Models/Order.cs` | Tiene `KitchenStatus` — estado global de preparación; la impresión es más granular |
| `KitchenStatus` (enum) | `POS.Domain/Enums/KitchenStatus.cs` | Cubre `Pending / Ready / Delivered`. **No se modifica**; los `PrintJob` son ortogonales a este estado |
| `OrderService` | `POS.Services/Service/OrderService.cs` | Punto de entrada donde se insertará la lógica de creación de `PrintJob`s |
| `IUnitOfWork` | `POS.Repository/IUnitOfWork.cs` | Necesita nueva propiedad `IPrintJobRepository PrintJobs` |

### 2.2 Gaps identificados

| # | Gap | Impacto |
|---|-----|---------|
| G-1 | `Product` no tiene `PrintingDestination` | Sin clasificación, no es posible dividir ítems por área |
| G-2 | No existe modelo `PrintJob` | Sin trazabilidad de qué se imprimió, cuándo y dónde |
| G-3 | No existe enum `PrintJobStatus` | El estado se maneja como string libre o no existe |
| G-4 | `OrderService` no crea trabajos de impresión al confirmar la orden | La impresión es manual o inexistente |
| G-5 | No existen endpoints para que periféricos consulten y confirmen impresión | Las impresoras/KDS no tienen API de polling |

---

## 3. Arquitectura de Dominio

### 3.1 Nuevo Enum: `PrintingDestination`

**Ruta:** `POS.Domain/Enums/PrintingDestination.cs`

Define dónde debe imprimirse el ticket de un producto. El valor por defecto es `Kitchen` para mantener compatibilidad con el flujo existente.

```
PrintingDestination (enum)
├── Kitchen  = 0   — Productos de cocina caliente / fría
├── Bar      = 1   — Bebidas, cafés, jugos
└── Waiters  = 2   — Ítems que no se preparan pero se despachan desde el piso
                     (ej. agua embotellada, complementos envasados)
```

**Decisión de diseño:** Se usa `enum` en lugar de `string` libre para garantizar consistencia en migraciones, reportes y serialización JSON. El valor entero persiste en la base de datos.

---

### 3.2 Modificación al Modelo `Product`

**Ruta:** `POS.Domain/Models/Product.cs`

Se agrega **un único campo** al modelo existente. No hay breaking changes en las columnas actuales.

```
Product (modificación)
└── PrintingDestination   PrintingDestination, default = Kitchen
                          Determina a qué área se envía el ticket cuando
                          este producto aparece en una orden.
```

**Navegación:** `OrderItem.Product` ya está declarada como `virtual Product? Product`, por lo que la propiedad estará disponible al hacer `.Include(i => i.Product)` en el Sync Engine.

---

### 3.3 Nuevo Enum: `PrintJobStatus`

**Ruta:** `POS.Domain/Enums/PrintJobStatus.cs`

```
PrintJobStatus (enum)
├── Pending  = 0   — Creado, esperando que la impresora/KDS lo tome
├── Printed  = 1   — Confirmado por el cliente (impresora o tablet)
└── Failed   = 2   — Falló tras el máximo de intentos permitidos
```

---

### 3.4 Nuevo Modelo: `PrintJob`

**Ruta:** `POS.Domain/Models/PrintJob.cs`

Representa un trabajo de impresión atómico: un subconjunto de ítems de una `Order` destinados a un área específica.

```
PrintJob
├── Id                int, PK, identity
├── OrderId           string(36), FK → Order, required
│                     — Vincula el trabajo a la orden original
├── BranchId          int, FK → Branch
│                     — Necesario para filtros multi-sucursal
├── Destination       PrintingDestination (enum, int column)
│                     — Área destino: Kitchen(0), Bar(1), Waiters(2)
├── Status            PrintJobStatus (enum, int column), default = Pending
│                     — Estado del ciclo de vida del trabajo
├── RawContent        string (nvarchar(max)), required
│                     — Contenido pre-renderizado del ticket en texto plano
│                       (ESC/POS, markdown simple o JSON estructurado).
│                       El backend no interpreta el formato, solo lo almacena
│                       para que el cliente lo envíe a la impresora.
├── CreatedAt         DateTime (UTC), default = UtcNow
│                     — Timestamp de creación, usado para ordering en polling
├── PrintedAt         DateTime? (UTC), nullable
│                     — Timestamp cuando el cliente confirmó la impresión
├── AttemptCount      int, default = 0
│                     — Número de veces que el cliente intentó imprimir este trabajo
│
├── Order             nav → Order
└── Branch            nav → Branch
```

**Sobre `RawContent`:** El formato exacto (texto ESC/POS, JSON enriquecido, etc.) se define en una fase posterior según el hardware target. En Fase 19 se usará texto plano estructurado. El campo `nvarchar(max)` garantiza que cualquier formato futuro quede contenido.

---

## 4. Lógica de Servicio — `OrderService`

### 4.1 Punto de intervención

La creación de `PrintJob`s se activa en el momento en que una orden cambia a estado "confirmado para preparación". Basado en el flujo actual del `OrderService`, el punto exacto de inserción es el método que procesa la sincronización de órdenes offline (`SyncOrdersAsync`) y/o el que promueve `KitchenStatus` de `Pending` a `Ready`.

> **Nota de implementación:** El análisis exacto del método receptor se realiza durante la Fase de Implementación. El diseño aquí es agnóstico al método de activación.

### 4.2 Algoritmo de agrupación

```
DADO una Order con Items[] cargados + Items[].Product cargados

1. Filtrar ítems activos (excluir cancelados si aplica)
2. Agrupar Items por Item.Product.PrintingDestination
   → Resultado: Dictionary<PrintingDestination, List<OrderItem>>
3. Por cada grupo (destination, items):
   a. Generar RawContent con el listado de ítems del grupo
      (Quantity × ProductName [SizeName] — Notes)
   b. Crear PrintJob {
        OrderId       = order.Id
        BranchId      = order.BranchId
        Destination   = destination
        Status        = Pending
        RawContent    = contenido generado en (a)
        CreatedAt     = DateTime.UtcNow
        AttemptCount  = 0
      }
4. Persistir todos los PrintJob en una sola transacción junto con la Order
   (misma llamada a SaveChangesAsync() del UnitOfWork)
```

**Regla de idempotencia:** Si ya existen `PrintJob`s para `(OrderId, Destination)` en estado `Pending` o `Printed`, no se crean duplicados. Se verifica antes de insertar.

### 4.3 Método privado de generación de contenido

```
GeneratePrintContent(PrintingDestination destination, List<OrderItem> items, Order order) → string

Formato de salida (texto plano, Fase 19):
  ─────────────────────────────
  ORDEN #1042   [COCINA]
  Mesa: 7       14:32
  ─────────────────────────────
  2x  Hamburguesa Clásica
        + Tamaño: Grande
        + Nota: sin cebolla
  1x  Papas Fritas
  ─────────────────────────────
```

El método es `private static` dentro del servicio; no expone lógica al exterior.

---

## 5. Repositorio e Infraestructura

### 5.1 `IPrintJobRepository`

**Ruta:** `POS.Repository/IRepository/IPrintJobRepository.cs`

Extiende `IGenericRepository<PrintJob>` con dos métodos específicos:

```
IPrintJobRepository : IGenericRepository<PrintJob>
├── GetPendingByBranchAsync(int branchId, PrintingDestination? destination)
│   → Task<IEnumerable<PrintJob>>
│   — Polling endpoint: devuelve trabajos en estado Pending, opcionalmente filtrados
│     por destino (la impresora de cocina solo pide Kitchen, etc.)
└── GetByOrderAsync(string orderId)
    → Task<IEnumerable<PrintJob>>
    — Para la vista de detalle de orden: muestra todos los trabajos relacionados
```

### 5.2 `IUnitOfWork` — nueva propiedad

```
IUnitOfWork
└── IPrintJobRepository PrintJobs { get; }   ← NUEVO
```

### 5.3 DbContext

Se agrega `DbSet<PrintJob> PrintJobs` al `ApplicationDbContext` y se configura la relación `PrintJob → Order (restrict delete)` y `PrintJob → Branch (cascade)`.

---

## 6. API Endpoints

**Controlador:** `PrintJobController`
**Ruta base:** `/api/print-jobs`

| Método | Ruta | Descripción | Quién consume |
|--------|------|-------------|---------------|
| `GET` | `/api/print-jobs/pending` | Devuelve los PrintJobs en estado `Pending` para la sucursal. Query params: `?destination=Kitchen` (opcional) | Impresora / KDS / Tablet |
| `PATCH` | `/api/print-jobs/{id}/printed` | Marca el PrintJob como `Printed`, registra `PrintedAt` | Impresora / KDS |
| `PATCH` | `/api/print-jobs/{id}/failed` | Incrementa `AttemptCount`, marca como `Failed` si supera el máximo (3) | Impresora / KDS |
| `GET` | `/api/print-jobs/by-order/{orderId}` | Lista todos los PrintJobs de una orden (para vista de detalle) | POS Frontend |

**Headers de autenticación:** Se heredan del esquema JWT existente. El `BranchId` se extrae del claim del token o se pasa como query param en una fase posterior.

---

## 7. Migración de Base de Datos

### 7.1 Cambios requeridos

| Operación | Detalle |
|-----------|---------|
| `ALTER TABLE Products` | Agregar columna `PrintingDestination int NOT NULL DEFAULT 0` |
| `CREATE TABLE PrintJobs` | Nueva tabla, campos del §3.4 |
| Índice en `PrintJobs` | `IX_PrintJobs_BranchId_Status` — optimiza el endpoint de polling |
| Índice en `PrintJobs` | `IX_PrintJobs_OrderId` — optimiza el lookup por orden |

### 7.2 Compatibilidad con datos existentes

- Todos los `Product`s existentes quedan con `PrintingDestination = 0` (Kitchen). Comportamiento idéntico al flujo actual → **sin breaking changes**.
- No hay `PrintJob`s históricos. La tabla nace vacía.

---

## 8. Flujo de Datos End-to-End

```
[Frontend POS]
    │
    │  POST /api/orders/sync  (o confirmación de orden)
    ▼
[OrderService.ConfirmOrderAsync()]
    │
    ├── 1. Carga Order con Items + Items.Product (Include)
    ├── 2. Llama GeneratePrintJobsAsync(order)
    │       ├── Agrupa ítems por Product.PrintingDestination
    │       ├── Genera RawContent por grupo
    │       └── Inserta PrintJob[] en UnitOfWork.PrintJobs
    └── 3. SaveChangesAsync() — todo en una transacción
    │
    ▼
[Base de datos]   PrintJobs: [{ Kitchen, Pending }, { Bar, Pending }]
    │
    │  GET /api/print-jobs/pending?destination=Kitchen
    ▼
[Impresora de Cocina / Tablet KDS]
    │
    │  PATCH /api/print-jobs/{id}/printed
    ▼
[PrintJob.Status = Printed, PrintedAt = UtcNow]
```

---

## 9. Decisiones de Diseño y Alternativas Descartadas

### 9.1 ¿Por qué Enum y no string para `PrintingDestination`?

| Criterio | Enum | String libre |
|----------|------|--------------|
| Consistencia en DB | ✅ Siempre int | ❌ "kitchen", "Kitchen", "KITCHEN" coexisten |
| Extensibilidad | ✅ Agregar valor + migración | ✅ Sin migración, pero sin contrato |
| Serialización JSON | ✅ `"Kitchen"` via `JsonStringEnumConverter` | ✅ Idéntico |
| Agrupación en LINQ | ✅ `.GroupBy(i => i.Product.PrintingDestination)` funciona sin parsing | ❌ Requiere normalización |

**Decisión:** Enum con valor explícito (`= 0`, `= 1`, `= 2`) para que los valores de DB sean estables entre entornos.

### 9.2 ¿Por qué no reusar `KitchenStatus` en `Order`?

`KitchenStatus` es un estado **agregado** de la orden completa (la cocina terminó TODO). Los `PrintJob`s son **granulares por área**. Mezclar ambos conceptos en una sola columna crearía ambigüedad cuando la Barra termina pero la Cocina no.

### 9.3 ¿`RawContent` como JSON estructurado o texto plano?

En Fase 19 se usa **texto plano** para minimizar acoplamiento con el hardware de impresoras. En fases futuras se puede migrar a JSON enriquecido sin cambiar el esquema (el campo es `nvarchar(max)`).

---

## 10. Archivos a Crear / Modificar

| Acción | Archivo |
|--------|---------|
| **CREAR** | `POS.Domain/Enums/PrintingDestination.cs` |
| **CREAR** | `POS.Domain/Enums/PrintJobStatus.cs` |
| **CREAR** | `POS.Domain/Models/PrintJob.cs` |
| **MODIFICAR** | `POS.Domain/Models/Product.cs` — agregar `PrintingDestination` |
| **CREAR** | `POS.Repository/IRepository/IPrintJobRepository.cs` |
| **CREAR** | `POS.Repository/Repository/PrintJobRepository.cs` |
| **MODIFICAR** | `POS.Repository/IUnitOfWork.cs` — agregar `PrintJobs` |
| **MODIFICAR** | `POS.Repository/UnitOfWork.cs` — implementar propiedad |
| **MODIFICAR** | `POS.Repository/ApplicationDbContext.cs` — agregar `DbSet<PrintJob>` |
| **MODIFICAR** | `POS.Services/Service/OrderService.cs` — agregar `GeneratePrintJobsAsync` |
| **MODIFICAR** | `POS.Services/IService/IOrderService.cs` — si aplica |
| **CREAR** | `POS.API/Controllers/PrintJobController.cs` |
| **CREAR** | Migración EF: `dotnet ef migrations add AddPrintingDestinationAndPrintJobs` |

---

## 11. Criterios de Aceptación

- [ ] Al confirmar una orden con ítems de Kitchen y Bar, se crean exactamente 2 `PrintJob`s en estado `Pending`.
- [ ] Si todos los ítems son Kitchen, se crea solo 1 `PrintJob` con destino Kitchen.
- [ ] Re-confirmar la misma orden no crea `PrintJob`s duplicados (idempotencia).
- [ ] `GET /api/print-jobs/pending?destination=Kitchen` devuelve solo los trabajos de Cocina.
- [ ] `PATCH /api/print-jobs/{id}/printed` actualiza `Status = Printed` y registra `PrintedAt`.
- [ ] `PATCH /api/print-jobs/{id}/failed` incrementa `AttemptCount` y marca `Failed` si `AttemptCount >= 3`.
- [ ] Productos existentes en la base de datos mantienen `PrintingDestination = Kitchen` post-migración.
- [ ] El campo `RawContent` contiene el listado de ítems legible (nombre, cantidad, notas).
