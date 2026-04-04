# Backend Design Document: Hybrid Counter-to-Table Flow

## Assign Table to an Existing Order

**Fecha:** 2026-04-02
**Autor:** Arquitecto Senior .NET
**Estado:** Pendiente de aprobación

---

## 1. Resumen Ejecutivo

Actualmente las órdenes creadas en modo **Counter** (sin mesa) no pueden transicionar al flujo **Restaurant** (con mesa). Este feature agrega un endpoint que permite asignar una mesa disponible a una orden activa que no tiene una asignada, actualizando ambas entidades de forma atómica con protección de concurrencia.

---

## 2. Modelos de Dominio Involucrados

### 2.1 Order (`POS.Domain/Models/Order.cs`)
- `TableId` (int?) — FK a `RestaurantTable`, actualmente `null` en órdenes Counter.
- `TableName` (string?, MaxLength 50) — nombre desnormalizado de la mesa.
- `UpdatedAt` (DateTime?) — se actualiza automáticamente vía `SaveChangesAsync` override en `ApplicationDbContext.cs`.
- **Concurrency token:** `xmin` (PostgreSQL xid) ya configurado en `OnModelCreating`.

### 2.2 RestaurantTable (`POS.Domain/Models/RestaurantTable.cs`)
- `Status` (string, MaxLength 20) — valores: `"available"` | `"occupied"`.
- `BranchId` (int) — debe coincidir con el `BranchId` de la orden.
- `IsActive` (bool) — solo mesas activas pueden asignarse.
- **Concurrency token:** `xmin` (PostgreSQL xid) ya configurado en `OnModelCreating`.

### 2.3 AuditLog (`POS.Domain/Models/AuditLog.cs`)
- El `AuditInterceptor` ya audita la entidad `Order` automáticamente en `SaveChangesAsync`. Registrará `Action = "Updated"` con `OldValues` / `NewValues` capturando los campos modificados (`TableId`, `TableName`).
- **No se necesita lógica adicional de auditoría manual** — el interceptor existente cubrirá este caso. El campo `Action` registrará `"Updated"` que es funcionalmente equivalente a "TableAssigned" (el delta en `OldValues`/`NewValues` mostrará que `TableId` pasó de `null` a un valor).

---

## 3. API Contract

### 3.1 Endpoint

| Aspecto | Valor |
|---------|-------|
| **Método** | `PATCH` |
| **Ruta** | `/api/orders/{id}/assign-table` |
| **Controlador** | `OrdersController` (existente) |
| **Autorización** | `[Authorize(Roles = "Owner,Manager,Cashier,Waiter")]` |
| **branchId** | Extraído del JWT via `BaseApiController.BranchId` — **nunca** del body/query |

### 3.2 Request DTO

Nuevo record `AssignTableRequest` definido en `OrdersController.cs` (al final del archivo, junto a los otros request DTOs del mismo controlador):

| Propiedad | Tipo | Validación | Descripción |
|-----------|------|------------|-------------|
| `TableId` | `int` | `[Required]` | ID de la mesa a asignar |

### 3.3 Response DTO

Nuevo record `AssignTableResult` definido en `IOrderService.cs` (junto a los otros result DTOs):

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `OrderId` | `string` | UUID de la orden actualizada |
| `TableId` | `int` | ID de la mesa asignada |
| `TableName` | `string` | Nombre de la mesa asignada |
| `TableStatus` | `string` | Nuevo status de la mesa (`"occupied"`) |

### 3.4 Response Status Codes

| Código | Condición |
|--------|-----------|
| **200 OK** | Mesa asignada exitosamente **O** la orden ya tiene esa misma mesa (idempotencia) |
| **400 Bad Request** | La orden ya tiene una mesa **diferente** asignada, o la orden está pagada/cancelada, o la mesa no está activa, o la mesa pertenece a otro branch |
| **404 Not Found** | Orden o mesa no encontrada |
| **409 Conflict** | `DbUpdateConcurrencyException` — la orden o mesa fue modificada por otro usuario |

### 3.5 Response Body (ejemplo exitoso)

```json
{
  "orderId": "a1b2c3d4-...",
  "tableId": 7,
  "tableName": "Mesa 7",
  "tableStatus": "occupied"
}
```

### 3.6 Response Body (ejemplo error)

Se maneja por el `ExceptionMiddleware` existente:

```json
{
  "error": "ValidationError",
  "message": "Order already assigned to a different table (Mesa 3).",
  "statusCode": 400
}
```

---

## 4. Flujo de Lógica de Negocio

### 4.1 Diagrama de Secuencia

```
Controller                    Service                         Repository / UoW
    |                            |                                |
    |-- PATCH assign-table ----->|                                |
    |   (orderId, tableId,       |                                |
    |    branchId from JWT)      |                                |
    |                            |-- BeginTransactionAsync() ---->|
    |                            |                                |
    |                            |-- GetAsync(orderId) --------->|
    |                            |   [tracked, with xmin]         |
    |                            |                                |
    |                            |-- Validate Order:              |
    |                            |   - exists?                    |
    |                            |   - belongs to branchId?       |
    |                            |   - not cancelled?             |
    |                            |   - not fully paid?            |
    |                            |   - idempotent check           |
    |                            |   - different table check      |
    |                            |                                |
    |                            |-- GetByIdAsync(tableId) ----->|
    |                            |   [tracked, with xmin]         |
    |                            |                                |
    |                            |-- Validate Table:              |
    |                            |   - exists?                    |
    |                            |   - belongs to branchId?       |
    |                            |   - IsActive?                  |
    |                            |   - Status == "available"?     |
    |                            |                                |
    |                            |-- Mutate:                      |
    |                            |   order.TableId = tableId      |
    |                            |   order.TableName = table.Name |
    |                            |   table.Status = "occupied"    |
    |                            |                                |
    |                            |-- Update(order) -------------->|
    |                            |-- Update(table) -------------->|
    |                            |                                |
    |                            |-- SaveChangesAsync() --------->|
    |                            |   [xmin check on both]         |
    |                            |   [AuditInterceptor fires]     |
    |                            |                                |
    |                            |-- CommitAsync() -------------->|
    |                            |                                |
    |<-- 200 OK + result --------|                                |
```

### 4.2 Reglas de Validación (en orden)

1. **Orden no encontrada** → `NotFoundException`
2. **Orden no pertenece al branch** (comparar `order.BranchId` vs JWT `branchId`) → `UnauthorizedException`
3. **Orden cancelada** (`CancelledAt != null`) → `ValidationException("Cannot assign table to a cancelled order.")`
4. **Orden completamente pagada** (`IsPaid == true`) → `ValidationException("Cannot assign table to a fully paid order.")`
5. **Idempotencia:** `order.TableId == request.TableId` → retornar `200 OK` con el resultado actual sin modificar nada (no iniciar transacción siquiera, retorno temprano antes del `BeginTransactionAsync`)
6. **Orden ya tiene otra mesa** (`order.TableId != null && order.TableId != request.TableId`) → `ValidationException("Order already assigned to a different table ({order.TableName}).")`
7. **Mesa no encontrada** → `NotFoundException`
8. **Mesa no pertenece al branch** (`table.BranchId != branchId`) → `ValidationException("Table does not belong to this branch.")`
9. **Mesa inactiva** (`!table.IsActive`) → `ValidationException("Table is not active.")`
10. **Mesa ocupada** (`table.Status != "available"`) → `ValidationException("Table is already occupied.")`

### 4.3 Concurrencia

- Ambas entidades (`Order` y `RestaurantTable`) ya tienen concurrency tokens `xmin` configurados en el `DbContext`.
- El `SaveChangesAsync` detectará automáticamente si cualquiera de las dos filas fue modificada por otro proceso.
- En caso de `DbUpdateConcurrencyException`: se rethrow como `ConcurrencyConflictException` (nueva excepción) que el middleware mapeará a **409 Conflict**.
- **Patrón existente a seguir:** ver `CashRegisterService.cs:108` y `OrderService.cs:337` — actualmente lanzan `ValidationException` que produce `400`. Para esta feature, necesitamos `409` para diferenciar conflictos de concurrencia de errores de validación.

### 4.4 Transaccionalidad

- Toda la operación se envuelve en `IUnitOfWork.BeginTransactionAsync()`.
- Un solo `SaveChangesAsync()` persiste ambos cambios (Order + Table).
- `CommitAsync()` finaliza la transacción.
- Si cualquier excepción ocurre, el `await using` del transaction asegura el rollback automático.

---

## 5. Cambios Requeridos por Capa

### 5.1 POS.Domain (Capa de Dominio)

| Archivo | Cambio | Detalles |
|---------|--------|----------|
| `Exceptions/ConcurrencyConflictException.cs` | **CREAR** | Nueva excepción que hereda de `Exception`. Mensaje: "The resource was modified by another user. Please refresh and try again." |

**Justificación:** Actualmente `DbUpdateConcurrencyException` se rethrow como `ValidationException` (400), pero semánticamente un conflicto de concurrencia es un **409 Conflict**, no un 400. Esta nueva excepción permite al middleware diferenciarlos.

### 5.2 POS.API (Capa de Presentación)

| Archivo | Cambio | Detalles |
|---------|--------|----------|
| `POS.API/Controllers/OrdersController.cs` | **MODIFICAR** | Agregar action method `AssignTable` + request DTO `AssignTableRequest` |
| `POS.API/Middleware/ExceptionMiddleware.cs` | **MODIFICAR** | Agregar catch para `ConcurrencyConflictException` → 409 Conflict |

**Action Method `AssignTable`:**
- Ruta: `PATCH {id}/assign-table`
- Roles: `Owner,Manager,Cashier,Waiter`
- Valida `ModelState`, llama a `_orderService.AssignTableAsync(id, request.TableId, BranchId)`
- Retorna `Ok(result)`
- Produce: `200`, `400`, `404`, `409`

**Nuevo catch en `ExceptionMiddleware`:**
- Interceptar `ConcurrencyConflictException` **antes** del catch genérico de `Exception`
- Mapear a `HttpStatusCode.Conflict` (409), error type `"ConcurrencyConflict"`

### 5.3 POS.Services (Capa de Servicios)

| Archivo | Cambio | Detalles |
|---------|--------|----------|
| `POS.Services/IService/IOrderService.cs` | **MODIFICAR** | Agregar firma `AssignTableAsync` + DTO `AssignTableResult` |
| `POS.Services/Service/OrderService.cs` | **MODIFICAR** | Implementar `AssignTableAsync` |

**Firma del método:**
```
Task<AssignTableResult> AssignTableAsync(string orderId, int tableId, int branchId)
```

**Implementación — pseudocódigo:**

```
1. Fetch order (tracked) via _unitOfWork.Orders.GetAsync(filter: o => o.Id == orderId)
2. Validate order (rules 1-6 from section 4.2)
3. If idempotent (rule 5): return early with AssignTableResult from current state
4. Begin transaction via _unitOfWork.BeginTransactionAsync()
5. Fetch table (tracked) via _unitOfWork.RestaurantTables.GetByIdAsync(tableId)
6. Validate table (rules 7-10 from section 4.2)
7. Mutate: order.TableId = tableId, order.TableName = table.Name
8. Mutate: table.Status = "occupied"
9. _unitOfWork.Orders.Update(order)
10. _unitOfWork.RestaurantTables.Update(table)
11. try: _unitOfWork.SaveChangesAsync()
12. catch DbUpdateConcurrencyException: throw new ConcurrencyConflictException(...)
13. transaction.CommitAsync()
14. Return AssignTableResult
```

**Nota sobre idempotencia:** La verificación idempotente (paso 2-3) ocurre **antes** de iniciar la transacción para evitar adquirir locks innecesarios.

### 5.4 POS.Repository (Capa de Datos)

**No se requieren cambios.** Las interfaces y repositorios existentes ya proporcionan:
- `IOrderRepository` hereda `GetAsync(filter, includes)` de `IGenericRepository<Order>` — suficiente para cargar la orden tracked.
- `IRestaurantTableRepository` hereda `GetByIdAsync(int id)` de `IGenericRepository<RestaurantTable>` — suficiente para cargar la mesa tracked.
- `IUnitOfWork.BeginTransactionAsync()` ya existe.
- Los concurrency tokens `xmin` ya están configurados para ambas entidades.

---

## 6. Auditoría

El `AuditInterceptor` existente en `POS.Repository/Interceptors/AuditInterceptor.cs` ya intercepta `Order` en `EntityState.Modified`. Cuando `SaveChangesAsync` se ejecute:

- **Action:** `"Updated"`
- **OldValues:** `{ "TableId": null, "TableName": null }`
- **NewValues:** `{ "TableId": 7, "TableName": "Mesa 7" }`
- **BranchId:** Extraído automáticamente de `order.BranchId`

No se necesita código adicional de auditoría.

---

## 7. Escenarios de Concurrencia

| Escenario | Resultado Esperado |
|-----------|--------------------|
| Cajero A y Cajero B intentan asignar la **misma mesa** a **diferentes órdenes** simultáneamente | El primero en hacer `SaveChanges` gana. El segundo recibe `409 Conflict` porque el `xmin` de `RestaurantTable` cambió (status pasó de "available" a "occupied"). |
| Cajero A asigna mesa a orden X mientras Cajero B modifica la misma orden X (e.g., agrega items) | El primero en hacer `SaveChanges` gana. El segundo recibe `409 Conflict` porque el `xmin` de `Order` cambió. |
| Cajero A intenta asignar mesa ya ocupada (sin race condition) | Falla en validación paso 10: `400 Bad Request` — "Table is already occupied." |
| Cajero A asigna Mesa 7 a Orden X, luego Cajero A intenta la misma operación de nuevo | Idempotente: `200 OK` sin modificar nada (retorno temprano). |

---

## 8. Resumen de Archivos a Crear/Modificar

| Archivo | Acción | LOC estimado |
|---------|--------|-------------|
| `POS.Domain/Exceptions/ConcurrencyConflictException.cs` | **CREAR** | ~8 |
| `POS.API/Middleware/ExceptionMiddleware.cs` | **MODIFICAR** | +5 (nuevo catch) |
| `POS.API/Controllers/OrdersController.cs` | **MODIFICAR** | +30 (action + DTO) |
| `POS.Services/IService/IOrderService.cs` | **MODIFICAR** | +15 (firma + result DTO) |
| `POS.Services/Service/OrderService.cs` | **MODIFICAR** | +55 (implementación) |

**Total:** 1 archivo nuevo, 4 archivos modificados. ~113 líneas netas.

---

## 9. Consideraciones Adicionales

### 9.1 PosExperience
El modelo `Order` actualmente **no tiene** una propiedad `PosExperience`. Este campo existe en `BusinessTypeCatalog` y `BranchConfigDto` a nivel de configuración de sucursal, no a nivel de orden individual. **No se agrega esta propiedad** a la orden — la transición Counter→Restaurant se refleja implícitamente por la presencia de `TableId` en una orden que originalmente no tenía uno.

### 9.2 Liberación de mesa al cancelar/pagar
Este documento cubre únicamente la **asignación** de mesa. La liberación de la mesa cuando la orden se paga o cancela es responsabilidad de los flujos existentes (`CancelAsync`, sync de pagos). Si estos flujos no manejan la liberación de mesa actualmente, eso sería un feature separado.

### 9.3 Impacto en frontend
El frontend (Angular) necesitará:
- Un botón "Asignar Mesa" visible en órdenes sin mesa
- Un selector de mesas disponibles (ya existe el endpoint `GET /api/table/status`)
- Manejo del `409 Conflict` con retry UX

Esto queda fuera del alcance de este documento backend.
