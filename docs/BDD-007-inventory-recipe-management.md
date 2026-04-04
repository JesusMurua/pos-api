# BDD-007 — Inventory & Recipe Management
## Fase 18: Insumos, Recetas y Descuento Automático de Inventario

**Fecha:** 2026-04-03
**Estado:** Diseño — Pendiente de aprobación
**Dependencias:** BDD-001 (Products), BDD-003 (Orders / Sync Engine)

---

## 1. Objetivo

Formalizar y extender el sistema de inventario que existe de manera parcial en la base de código.
Al finalizar la Fase 18, el negocio podrá:

- Gestionar **Insumos** (materias primas) con unidad de medida tipada.
- Definir **Recetas** que vinculan un `Product` con los insumos que consume.
- Ver un **libro contable inmutable** de cada movimiento de inventario (compras, mermas, consumo por venta, ajustes manuales).
- Confiar en que cada venta sincronizan descontará automáticamente el stock de insumos a través del Sync Engine.

---

## 2. Auditoría del Estado Actual

Antes de diseñar nuevas entidades, se auditó lo existente para no duplicar trabajo.

### 2.1 Lo que ya existe y funciona

| Artefacto | Ubicación | Estado |
|-----------|-----------|--------|
| `InventoryItem` | `POS.Domain/Models/InventoryItem.cs` | ✅ Existe — equivale al "Ingredient" del objetivo |
| `InventoryMovement` | `POS.Domain/Models/InventoryMovement.cs` | ⚠️ Existe — ledger primitivo, requiere mejoras |
| `ProductConsumption` | (referenciado en servicio) | ✅ Existe — equivale al "RecipeItem" del objetivo |
| `InventoryService` | `POS.Services/Service/InventoryService.cs` | ✅ Funcional — con `DeductBatchCoreAsync` |
| `InventoryController` | `POS.API/Controllers/InventoryController.cs` | ✅ Funcional — CRUD completo |
| Phase 6 en SyncEngine | `OrderService.cs:417` | ✅ Ya llama `DeductFromOrdersBatchAsync` |

### 2.2 Gaps identificados

| # | Gap | Impacto |
|---|-----|---------|
| G-1 | `Unit` en `InventoryItem` es un `string` libre, no un Enum tipado | Inconsistencias de datos, dificulta reportes ("kg", "Kg", "KG" coexisten) |
| G-2 | `Type` en `InventoryMovement` es un `string` libre ("in", "out", "adjustment") | Sin semántica de negocio; "adjustment" sobrescribe stock (no es delta) |
| G-3 | `InventoryMovement` no registra `StockAfterTransaction` | El ledger no es auto-suficiente; reconstruir histórico requiere replay |
| G-4 | `InventoryMovement` no tiene `CreatedBy` (string) | Falta trazabilidad de auditoría para mermas y compras manuales |
| G-5 | No existe endpoint para registrar **compras** (Purchase) con costo variable | No hay flujo de entrada de stock desde la UI |
| G-6 | No existe endpoint para registrar **mermas** (Waste) con nota obligatoria | No hay flujo de baja controlada de insumos |
| G-7 | No existe historial de movimientos con filtros de fecha y tipo | Los reportes de inventario quedan bloqueados |
| G-8 | `ProductConsumption` no está documentado formalmente en el diseño | El concepto "Receta" es opaco para el equipo frontend |

---

## 3. Arquitectura de Dominio

### 3.1 Entidades existentes que SE MANTIENEN (sin breaking changes)

#### `InventoryItem` (= Ingredient)

Se mantiene la entidad actual. Se agregará la migración para cambiar `Unit` a un Enum en la base de datos. La columna existente se convertirá.

```
InventoryItem
├── Id                  int, PK, identity
├── BranchId            int, FK → Branch
├── Name                string(100), required — Ej: "Harina de trigo", "Pollo crudo"
├── Unit                string(10) → MIGRAR a UnitOfMeasure enum (ver §3.2)
├── CurrentStock        decimal — Stock actual (mantiene el running total)
├── LowStockThreshold   decimal — Umbral para alertas
├── CostCents           int — Costo por unidad en centavos (costo de adquisición)
├── IsActive            bool
├── CreatedAt           DateTime
├── UpdatedAt           DateTime
│
├── Branch              nav → Branch
├── Movements           nav → ICollection<InventoryMovement>
└── ProductConsumptions nav → ICollection<ProductConsumption>
```

#### `ProductConsumption` (= RecipeItem)

Se mantiene sin cambios estructurales. Es el pivot entre `Product` e `InventoryItem`.

```
ProductConsumption
├── Id                int, PK, identity
├── ProductId         int, FK → Product
├── InventoryItemId   int, FK → InventoryItem
└── QuantityPerSale   decimal — Cantidad consumida POR UNIDAD vendida
                               Ej: 0.2 significa 200g por cada 1 unidad vendida
```

**Relación EF Core:**
- `Product` → `ICollection<ProductConsumption>` (1:N)
- `InventoryItem` → `ICollection<ProductConsumption>` (1:N)
- Índice único: `(ProductId, InventoryItemId)` — una regla por par

#### `InventoryMovement` (= InventoryTransaction / Ledger)

Se enriquece sin eliminar columnas existentes para preservar compatibilidad.

```
InventoryMovement
├── Id                      int, PK, identity
├── InventoryItemId         int?, FK → InventoryItem  (null si el mov. es sobre Product.TrackStock)
├── ProductId               int?, FK → Product         (solo para path TrackStock directo)
│
│   ── Campos a AGREGAR en Fase 18 ──
├── TransactionType         InventoryTransactionType enum (ver §3.2) — REEMPLAZA Type string
├── StockAfterTransaction   decimal — Snapshot del stock DESPUÉS del movimiento (ledger inmutable)
├── CreatedBy               string(100)? — Username o "SyncEngine" para trazabilidad
│   ────────────────────────────────
│
├── Type                    string(20) — se mantiene por compatibilidad; se poblará desde TransactionType
├── Quantity                decimal — SIEMPRE positivo; la dirección la da TransactionType
├── Reason                  string(500)? — Nota libre (obligatoria para Waste)
├── OrderId                 string(36)? — FK referencial → Order.Id (para ConsumeFromSale)
└── CreatedAt               DateTime
```

> **Regla de inmutabilidad del ledger:** Los movimientos NUNCA se actualizan ni eliminan. Para corregir un error se registra un movimiento de ajuste inverso. El repositorio `IInventoryMovementRepository` NO expondrá métodos `Update()` ni `Delete()`.

### 3.2 Nuevos Enums necesarios

#### `UnitOfMeasure` (nuevo)

```
POS.Domain/Enums/UnitOfMeasure.cs

Kg     = 0   // Kilogramos
G      = 1   // Gramos
L      = 2   // Litros
mL     = 3   // Mililitros
Pcs    = 4   // Piezas / unidades
Oz     = 5   // Onzas
```

**Estrategia de migración:** `Unit` string → `UnitOfMeasure` enum.
- Se agrega columna `UnitOfMeasure int` con valor default `4` (Pcs).
- Se ejecuta script de conversión en la migración.
- La columna `Unit` se puede marcar deprecated pero se mantiene durante Fase 18 para no romper clientes.

#### `InventoryTransactionType` (nuevo)

```
POS.Domain/Enums/InventoryTransactionType.cs

Purchase         = 0   // Entrada de compra — suma stock
ConsumeFromSale  = 1   // Descuento automático por venta — resta stock
Waste            = 2   // Merma/baja controlada — resta stock (requiere Reason)
ManualAdjustment = 3   // Corrección manual — puede ser positivo o negativo (requiere Reason)
InitialCount     = 4   // Conteo inicial al crear el insumo — establece stock base
```

**Mapeo con Type string existente:**
| TransactionType | Type legacy |
|-----------------|-------------|
| Purchase | "in" |
| ConsumeFromSale | "out" |
| Waste | "out" |
| ManualAdjustment | "adjustment" (ahora DELTA, no sobrescritura) |
| InitialCount | "in" |

> **Cambio semántico crítico:** El tipo `adjustment` anteriormente sobrescribía el stock (`item.CurrentStock = quantity`). A partir de Fase 18, `ManualAdjustment` opera como **delta** (positivo o negativo). Para establecer un conteo exacto se usa `InitialCount` con delta calculado por el servicio.

---

## 4. Relaciones Entity Framework

```
Branch ─────────────────────────────────────────────────────┐
  │ 1:N                                                       │
  ├── InventoryItem                                           │
  │     │ 1:N                                                 │
  │     ├── InventoryMovement                                  │
  │     └── ProductConsumption                                │
  │           │ N:1                                           │
  └── Product ┘ (también Branch 1:N)                         │
        │ 1:N                                                 │
        ├── ProductConsumption                                │
        ├── OrderItem ──────── Order ──── Branch ────────────┘
        └── (TrackStock direct path → InventoryMovement via ProductId)
```

### Configuraciones EF Core relevantes

```
// ProductConsumption: índice único para evitar duplicados de receta
modelBuilder.Entity<ProductConsumption>()
    .HasIndex(pc => new { pc.ProductId, pc.InventoryItemId })
    .IsUnique();

// InventoryMovement: sin restricción de FK dura en OrderId
// (las órdenes tienen Id tipo string; se guarda como referencia informativa)
modelBuilder.Entity<InventoryMovement>()
    .HasIndex(m => m.OrderId);

// InventoryMovement: sin cascada delete — los movimientos son permanentes
modelBuilder.Entity<InventoryMovement>()
    .HasOne(m => m.InventoryItem)
    .WithMany(i => i.Movements)
    .HasForeignKey(m => m.InventoryItemId)
    .OnDelete(DeleteBehavior.Restrict);
```

---

## 5. Contratos de Servicio — `IInventoryService`

Se documenta el contrato completo incluyendo los métodos actuales y los nuevos de Fase 18.

### 5.1 Métodos existentes (sin cambios de firma)

```csharp
// CRUD de insumos
Task<IEnumerable<InventoryItem>> GetAllAsync(int branchId);
Task<InventoryItem> GetByIdAsync(int id);
Task<IEnumerable<InventoryItem>> GetLowStockAsync(int branchId);
Task<InventoryItem> CreateAsync(InventoryItem item);
Task<InventoryItem> UpdateAsync(int id, InventoryItem item);
Task<bool> DeleteAsync(int id);      // soft-delete

// Movimientos de stock
Task<InventoryMovement> AddMovementAsync(
    int itemId, string type, decimal quantity, string? reason, string? orderId);

// Recetas (ProductConsumption)
Task<IEnumerable<ProductConsumption>> GetConsumptionByProductAsync(int productId);
Task<ProductConsumption> CreateConsumptionAsync(
    int productId, int inventoryItemId, decimal quantityPerSale);
Task<bool> DeleteConsumptionAsync(int id);

// Deducción por venta (llamado por Sync Engine)
Task DeductFromSaleAsync(string orderId, List<SaleItem> items);
Task DeductFromOrdersBatchAsync(List<Order> orders);

// Utilidades
Task<IEnumerable<int>> GetOutOfStockProductIdsAsync(int branchId);
Task<IEnumerable<InventoryMovement>> GetMovementsAsync(int itemId);
Task<IEnumerable<InventoryMovement>> GetProductMovementsAsync(int productId);
```

### 5.2 Nuevos métodos a agregar en Fase 18

```csharp
// Registrar compra de insumos (Purchase)
// Suma stock + crea InventoryMovement(Purchase) + registra StockAfterTransaction
Task<InventoryMovement> RegisterPurchaseAsync(
    int inventoryItemId,
    decimal quantity,
    int? costCentsPerUnit,   // Si es null, no actualiza CostCents existente
    string? note,
    string createdBy);

// Registrar merma (Waste) — reason es obligatorio
Task<InventoryMovement> RegisterWasteAsync(
    int inventoryItemId,
    decimal quantity,
    string reason,           // [Required] — "Producto caduco", "Accidente", etc.
    string createdBy);

// Ajuste manual de stock (delta positivo o negativo)
Task<InventoryMovement> RegisterManualAdjustmentAsync(
    int inventoryItemId,
    decimal delta,            // Positivo = suma, negativo = resta
    string reason,            // [Required]
    string createdBy);

// Historial de movimientos con filtros
Task<IEnumerable<InventoryMovement>> GetMovementHistoryAsync(
    int branchId,
    int? inventoryItemId,        // null = todos los insumos de la branch
    InventoryTransactionType? type,
    DateTime? from,
    DateTime? to);

// Lista de productos a los que le falta receta
Task<IEnumerable<Product>> GetProductsWithoutRecipeAsync(int branchId);
```

### 5.3 Lógica interna de `RegisterPurchaseAsync`

```
1. Cargar InventoryItem por id — lanzar NotFoundException si no existe
2. Calcular stockAntes = item.CurrentStock
3. item.CurrentStock += quantity
4. Si costCentsPerUnit != null → item.CostCents = costCentsPerUnit (precio actualizado)
5. item.UpdatedAt = UtcNow
6. Crear InventoryMovement:
   - TransactionType = Purchase
   - Type = "in"  (legacy, para compatibilidad)
   - Quantity = quantity (siempre positivo)
   - StockAfterTransaction = item.CurrentStock
   - Reason = note
   - CreatedBy = createdBy
   - CreatedAt = UtcNow
7. _unitOfWork.Inventory.Update(item)
8. await _unitOfWork.InventoryMovements.AddAsync(movement)
9. await _unitOfWork.SaveChangesAsync()
10. Return movement
```

### 5.4 Lógica de `RegisterManualAdjustmentAsync`

```
1. Cargar InventoryItem
2. stockAntes = item.CurrentStock
3. item.CurrentStock += delta   // DELTA, no sobrescritura
4. item.UpdatedAt = UtcNow
5. Crear InventoryMovement:
   - TransactionType = ManualAdjustment
   - Type = delta >= 0 ? "in" : "out"  (legacy)
   - Quantity = Math.Abs(delta)         (legacy: siempre positivo)
   - StockAfterTransaction = item.CurrentStock
   - Reason = reason  (obligatorio)
   - CreatedBy = createdBy
6. Guardar atómicamente
```

---

## 6. Sync Engine — Integración con Phase 6

### 6.1 Estado actual

```
Phase 6 (OrderService.cs:417):
    if (ordersToInsert.Count > 0)
        await _inventoryService.DeductFromOrdersBatchAsync(ordersToInsert);
```

La integración **ya está hecha** y funciona. `DeductBatchCoreAsync` en `InventoryService` implementa:

- **Path A (TrackStock directo):** Cuando `Product.TrackStock == true`, descuenta de `Product.CurrentStock` directamente. Crea `InventoryMovement` con `ProductId` (sin `InventoryItemId`).
- **Path B (Recipe-based):** Busca `ProductConsumption` del producto, carga `InventoryItem`s, descuenta `QuantityPerSale × Quantity` de cada insumo. Crea `InventoryMovement` con `InventoryItemId`.

### 6.2 Mejoras a aplicar en `DeductBatchCoreAsync` en Fase 18

El método se actualiza **internamente** para poblar los campos nuevos del ledger, sin cambiar la firma:

```
Cambio en el movimiento de Path A (TrackStock):
  ANTES: movements.Add(new InventoryMovement { Type = "out", ... })
  DESPUÉS: + TransactionType = ConsumeFromSale
           + StockAfterTransaction = product.CurrentStock  (post-deducción)
           + CreatedBy = "SyncEngine"

Cambio en el movimiento de Path B (Recipe):
  ANTES: movements.Add(new InventoryMovement { Type = "out", ... })
  DESPUÉS: + TransactionType = ConsumeFromSale
           + StockAfterTransaction = invItem.CurrentStock  (post-deducción)
           + CreatedBy = "SyncEngine"
```

No se necesita una nueva Phase 6b ni Phase 7 en el Sync Engine. La deducción ya es atómica con bulk insert + `SaveChangesAsync`.

### 6.3 Atomicidad y manejo de errores

La deducción de inventario es **best-effort** (la venta no falla si el inventario falla). Esto es correcto para un sistema POS offline-first. El `try/catch` alrededor de `DeductFromOrdersBatchAsync` en Phase 6 se mantiene.

```
Política de stock negativo:
- Se PERMITE stock negativo (ventas fuera de línea no tienen forma de validar stock en tiempo real)
- El dashboard de inventario mostrará stock en negativo como alerta visual
- No se bloquean ventas por stock insuficiente (regla de negocio por diseño)
```

---

## 7. Contratos de API — `InventoryController`

### 7.1 Endpoints existentes (se mantienen)

| Método | Ruta | Descripción | Roles |
|--------|------|-------------|-------|
| GET | `/api/inventory` | Lista todos los insumos activos | Owner,Manager,Cashier,Kitchen,Waiter |
| GET | `/api/inventory/{id}` | Insumo por ID | Owner,Manager |
| GET | `/api/inventory/low-stock` | Insumos bajo umbral | Owner,Manager |
| GET | `/api/inventory/out-of-stock-products` | ProductIds sin stock | Owner,Manager,Cashier,Kitchen,Waiter |
| GET | `/api/inventory/{id}/movements` | Movimientos de un insumo | Owner,Manager |
| POST | `/api/inventory/create` | Crear insumo | Owner,Manager |
| PUT | `/api/inventory/{id}` | Actualizar insumo | Owner,Manager |
| DELETE | `/api/inventory/{id}` | Soft-delete insumo | Owner,Manager |
| POST | `/api/inventory/{id}/movement` | Movimiento genérico (legacy) | Owner,Manager |
| GET | `/api/inventory/consumption/{productId}` | Receta de un producto | Owner,Manager |
| POST | `/api/inventory/consumption` | Crear/actualizar línea de receta | Owner,Manager |
| DELETE | `/api/inventory/consumption/{id}` | Eliminar línea de receta | Owner,Manager |
| POST | `/api/inventory/deduct-sale` | Deducción manual por venta | Owner,Manager,Cashier |

### 7.2 Nuevos endpoints a agregar en Fase 18

| Método | Ruta | Descripción | Roles |
|--------|------|-------------|-------|
| POST | `/api/inventory/{id}/purchase` | Registrar compra de insumo | Owner,Manager |
| POST | `/api/inventory/{id}/waste` | Registrar merma | Owner,Manager |
| POST | `/api/inventory/{id}/adjustment` | Ajuste manual de stock (delta) | Owner,Manager |
| GET | `/api/inventory/movements/history` | Historial con filtros de fecha/tipo | Owner,Manager |
| GET | `/api/inventory/products-without-recipe` | Productos sin receta asignada | Owner,Manager |

### 7.3 Request bodies para nuevos endpoints

#### `POST /api/inventory/{id}/purchase`

```json
{
  "quantity": 10.5,
  "costCentsPerUnit": 3200,   // null = mantiene costo existente
  "note": "Compra proveedor X"
}
```

#### `POST /api/inventory/{id}/waste`

```json
{
  "quantity": 0.5,
  "reason": "Producto caduco — fecha vencida 2026-04-01"  // REQUERIDO
}
```

#### `POST /api/inventory/{id}/adjustment`

```json
{
  "delta": -2.0,   // negativo = resta stock; positivo = suma stock
  "reason": "Corrección post-conteo físico del 2026-04-03"  // REQUERIDO
}
```

#### `GET /api/inventory/movements/history` (query params)

```
?inventoryItemId=5    (opcional — sin este param devuelve toda la branch)
&type=Purchase         (opcional — enum: Purchase|ConsumeFromSale|Waste|ManualAdjustment|InitialCount)
&from=2026-04-01T00:00:00Z
&to=2026-04-03T23:59:59Z
```

### 7.4 Respuesta estándar de movimiento

Todos los endpoints de movimiento devuelven el mismo DTO:

```json
{
  "id": 123,
  "inventoryItemId": 5,
  "inventoryItemName": "Harina de trigo",
  "transactionType": "Purchase",
  "quantity": 10.5,
  "stockAfterTransaction": 25.3,
  "reason": "Compra proveedor X",
  "orderId": null,
  "createdBy": "admin@empresa.com",
  "createdAt": "2026-04-03T14:32:00Z"
}
```

---

## 8. Repositorios — Cambios Necesarios

### 8.1 `IInventoryMovementRepository`

Se agrega método de historial con filtros:

```csharp
// Historial filtrado (implementado con Expression<Func<>> + Where encadenados)
Task<IEnumerable<InventoryMovement>> GetHistoryAsync(
    int branchId,
    int? inventoryItemId,
    InventoryTransactionType? type,
    DateTime? from,
    DateTime? to);
```

### 8.2 `IProductConsumptionRepository`

Sin cambios en firma. Solo verificar que existe `GetByProductAndItemAsync(int productId, int inventoryItemId)`.

### 8.3 `IInventoryRepository` (nuevo método)

```csharp
// Para el endpoint products-without-recipe
Task<IEnumerable<InventoryItem>> GetAllByBranchAsync(int branchId);  // ya existe
```

El cálculo de "productos sin receta" se hace en el servicio, no en el repositorio:

```
1. GetAsync todos los Products del branch con IsAvailable = true
2. GetAsync todos los ProductConsumptions donde ProductId está en esos productos
3. HashSet de ProductIds con al menos 1 ConsumptionRule
4. Devolver Products que NO estén en ese HashSet
```

---

## 9. Migraciones EF Core Necesarias

### Migración 1: `AddUnitOfMeasureToInventoryItem`

```
ALTER TABLE InventoryItems ADD UnitOfMeasure int NOT NULL DEFAULT 4  -- Pcs
-- Script de conversión de Unit string → UnitOfMeasure int
UPDATE InventoryItems SET UnitOfMeasure = 0 WHERE LOWER(Unit) IN ('kg', 'kilogramo', 'kilogramos')
UPDATE InventoryItems SET UnitOfMeasure = 1 WHERE LOWER(Unit) IN ('g', 'gramo', 'gramos')
UPDATE InventoryItems SET UnitOfMeasure = 2 WHERE LOWER(Unit) IN ('l', 'litro', 'litros')
UPDATE InventoryItems SET UnitOfMeasure = 3 WHERE LOWER(Unit) IN ('ml', 'mililitro', 'mililitros')
UPDATE InventoryItems SET UnitOfMeasure = 5 WHERE LOWER(Unit) IN ('oz', 'onza', 'onzas')
-- El resto queda como Pcs (4)
```

### Migración 2: `AddTransactionTypeAndAuditToInventoryMovement`

```
ALTER TABLE InventoryMovements ADD TransactionType int NOT NULL DEFAULT 1   -- ConsumeFromSale
ALTER TABLE InventoryMovements ADD StockAfterTransaction decimal(18,4) NOT NULL DEFAULT 0
ALTER TABLE InventoryMovements ADD CreatedBy nvarchar(100) NULL

-- Poblar TransactionType desde Type string existente (best-effort)
UPDATE InventoryMovements SET TransactionType = 0 WHERE Type = 'in'         -- Purchase
UPDATE InventoryMovements SET TransactionType = 1 WHERE Type = 'out'        -- ConsumeFromSale
UPDATE InventoryMovements SET TransactionType = 3 WHERE Type = 'adjustment' -- ManualAdjustment

-- Índice para queries de historial
CREATE INDEX IX_InventoryMovements_BranchDate
    ON InventoryMovements (CreatedAt DESC)
    INCLUDE (InventoryItemId, TransactionType)
```

---

## 10. Dual-Path del Motor de Descuento

El sistema soporta dos tipos de productos con lógicas de inventario distintas:

```
┌─────────────────────────────────────────────────────────────────┐
│                    DeductBatchCoreAsync                         │
│                                                                 │
│  Para cada (orderId, productId, quantity) en la venta:         │
│                                                                 │
│  ¿Product.TrackStock == true?                                   │
│     ├── SÍ → Path A: TrackStock                                │
│     │         Product.CurrentStock -= quantity                  │
│     │         InventoryMovement { ProductId, Type="out",        │
│     │           TransactionType=ConsumeFromSale, ... }          │
│     │         Si CurrentStock ≤ LowStockThreshold:             │
│     │           Product.IsAvailable = false                     │
│     │                                                           │
│     └── NO → Path B: Recipe-based                             │
│               Buscar ProductConsumptions del ProductId          │
│               Para cada ConsumptionRule:                        │
│                 InventoryItem.CurrentStock -= (QuantityPerSale  │
│                                               × orderQuantity)  │
│                 InventoryMovement { InventoryItemId, Type="out",│
│                   TransactionType=ConsumeFromSale, ... }        │
│                 Si stock ≤ LowStockThreshold: LOG warning       │
│                                                                 │
│  Bulk AddRangeAsync(movements) + SaveChangesAsync               │
└─────────────────────────────────────────────────────────────────┘
```

**Nota de diseño:** No se implementa bloqueo de venta por stock bajo en el Sync Engine. El sistema es offline-first y no puede garantizar stock en tiempo real. El descuento es best-effort y está envuelto en try/catch que registra warning sin propagar la excepción.

---

## 11. Plan de Implementación — Fase 18

### Sub-fase 18a: Fundación de Dominio

1. Crear `POS.Domain/Enums/UnitOfMeasure.cs`
2. Crear `POS.Domain/Enums/InventoryTransactionType.cs`
3. Agregar `UnitOfMeasure` a `InventoryItem` (manteniendo `Unit` string como deprecated)
4. Agregar `TransactionType`, `StockAfterTransaction`, `CreatedBy` a `InventoryMovement`
5. Generar migración `AddInventoryPhase18Fields`

### Sub-fase 18b: Servicios y Repositorios

6. Agregar `GetHistoryAsync` a `IInventoryMovementRepository` e implementación
7. Agregar métodos nuevos a `IInventoryService`: `RegisterPurchaseAsync`, `RegisterWasteAsync`, `RegisterManualAdjustmentAsync`, `GetMovementHistoryAsync`, `GetProductsWithoutRecipeAsync`
8. Implementar en `InventoryService`
9. Actualizar `DeductBatchCoreAsync` para poblar `TransactionType`, `StockAfterTransaction`, `CreatedBy`

### Sub-fase 18c: API

10. Agregar endpoints nuevos a `InventoryController`
11. Crear request/response DTOs para purchase, waste, adjustment, history
12. Actualizar documentación Swagger

---

## 12. Casos de Uso Cubiertos

| # | Caso de Uso | Endpoint |
|---|------------|---------|
| CU-1 | Chef crea receta: 1 Taco consume 0.1kg de carne | POST `/api/inventory/consumption` |
| CU-2 | Gerente registra llegada de 20kg de carne | POST `/api/inventory/{id}/purchase` |
| CU-3 | Cocinero reporta 200g de carne caducada | POST `/api/inventory/{id}/waste` |
| CU-4 | Socio hace conteo físico y ajusta stock | POST `/api/inventory/{id}/adjustment` |
| CU-5 | Venta de 5 Tacos descuenta 0.5kg de carne | SyncEngine Phase 6 (automático) |
| CU-6 | Gerente ve tendencia de consumo este mes | GET `/api/inventory/movements/history?type=ConsumeFromSale&from=...` |
| CU-7 | Sistema alerta carne por debajo de 2kg | GET `/api/inventory/low-stock` |
| CU-8 | Admin detecta qué productos no tienen receta | GET `/api/inventory/products-without-recipe` |

---

## 13. Decisiones de Diseño

| Decisión | Justificación |
|----------|--------------|
| No se crea tabla `Ingredient` nueva — se usa `InventoryItem` | Evita migración destructiva; el modelo existente es equivalente |
| No se crea tabla `RecipeItem` nueva — se usa `ProductConsumption` | Mismo razonamiento; el modelo ya está en producción |
| Stock negativo permitido | POS offline-first: la venta ocurre sin conexión; bloquear en sync rompería el flujo |
| Ledger inmutable (sin Delete en movimientos) | Trazabilidad contable; correcciones se hacen con ajustes inversos |
| `StockAfterTransaction` como snapshot | Permite auditar cualquier punto histórico sin replay completo |
| `ManualAdjustment` opera como delta | Consistente con el modelo ledger; el ajuste "absoluto" de stock anterior se reemplaza por `InitialCount` |
| Best-effort en Sync Engine Phase 6 | La venta no puede fallar por inventario; el stock incorrecto se resuelve operativamente |
