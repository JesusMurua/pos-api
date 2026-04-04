# Backend Design Document: Mandatory Cash Register Session for Local Orders

**Fecha:** 2026-04-03
**Autor:** Arquitecto Senior .NET
**Estado:** Pendiente de aprobación

---

## 1. Resumen Ejecutivo

Actualmente, `SyncOrdersAsync` acepta y persiste órdenes locales del POS sin validar que exista una sesión de caja abierta. Esto rompe la trazabilidad financiera: al cerrar la sesión (`CloseSessionAsync`), las ventas de cash se calculan retrospectivamente por ventana de tiempo (`OpenedAt → ClosedAt`), pero si no hubo sesión abierta durante la creación de la orden, esas ventas caen en un vacío financiero.

Este feature agrega una validación **temprana y fail-fast** en el Sync Engine: toda orden local (`OrderSource.Direct`) debe estar respaldada por una sesión de caja abierta en su branch.

---

## 2. Hallazgos Clave del Análisis

### 2.1 Estado actual — No hay vínculo Order ↔ CashRegisterSession

- El modelo `Order` (`POS.Domain/Models/Order.cs`) **no tiene** una propiedad `CashRegisterSessionId`.
- El modelo `SyncOrderRequest` (`POS.Domain/Models/SyncOrderRequest.cs`) **tampoco** la tiene.
- La relación es **indirecta y retrospectiva**: `CashRegisterService.CalculateCashSalesAsync` suma pagos en cash de órdenes creadas entre `OpenedAt` y `ClosedAt` de la sesión (`CashRegisterService.cs:182-195`).

### 2.2 Flujo de ingreso de órdenes — dos caminos disjuntos

| Origen | Endpoint | Modelo de entrada | OrderSource |
|--------|----------|-------------------|-------------|
| POS local (offline sync) | `POST /api/orders/sync` | `SyncOrderRequest` | **Implícito: `Direct`** (default en `Order.cs:57`) |
| Plataformas de delivery | `POST /api/delivery/ingest` | `IngestDeliveryOrderRequest` | Explícito: `UberEats`, `Rappi`, `DidiFood` |

**Hallazgo crítico:** `SyncOrderRequest` no tiene un campo `OrderSource`. Toda orden que entra por `/api/orders/sync` es implícitamente `OrderSource.Direct`. Esto significa que **todas las órdenes del Sync Engine son locales** y requieren sesión de caja. No necesitamos filtrar por `OrderSource`.

### 2.3 Unicidad de sesión

Un `filtered unique index` en la DB garantiza que solo existe **una sesión abierta por branch** en cualquier momento (`ApplicationDbContext.cs:400-402`):

```
HasIndex(s => s.BranchId).IsUnique().HasFilter("\"Status\" = 'open'")
```

Esto simplifica la validación: basta con llamar `GetOpenSessionAsync(branchId)` y verificar que no retorne `null`.

### 2.4 Repositorio existente

`ICashRegisterSessionRepository` ya expone `GetOpenSessionAsync(int branchId)` (`ICashRegisterSessionRepository.cs`), que retorna la sesión abierta o `null`. **No se necesitan cambios en el repositorio.**

---

## 3. Decisión Arquitectural: ¿Agregar FK `CashRegisterSessionId` a `Order`?

### Opción A: Solo validar existencia de sesión abierta (sin FK)
- Valida que haya sesión abierta al momento del sync.
- No persiste la relación. El cálculo de ventas sigue siendo retrospectivo por ventana temporal.
- **Ventaja:** Zero schema changes, zero migrations, implementación mínima.
- **Desventaja:** No hay vínculo directo; si una sesión se reabre/recrea, la contabilidad podría divergir.

### Opción B: Agregar FK `CashRegisterSessionId` a `Order` (recomendada)
- Valida que haya sesión abierta y **persiste el ID** en cada orden.
- La relación queda explícita en la base de datos.
- **Ventaja:** Trazabilidad directa, auditable, queries simples por sesión.
- **Desventaja:** Requiere migration + actualización de modelos + actualización del mapping.

### Recomendación: **Opción B**

El objetivo es "financial traceability". Una validación sin persistencia es un guardrail incompleto — no responde la pregunta "¿a qué sesión de caja pertenece esta orden?" después del hecho.

---

## 4. Modelos de Dominio Involucrados

### 4.1 Order (`POS.Domain/Models/Order.cs`)
- **AGREGAR** propiedad `CashRegisterSessionId` (`int?`) — FK nullable a `CashRegisterSession`.
- **AGREGAR** navegación `CashRegisterSession?` session.
- Es `int?` (nullable) porque las órdenes de **delivery** (`OrderSource != Direct`) ingresan vía `DeliveryService` y NO requieren sesión de caja.

### 4.2 SyncOrderRequest (`POS.Domain/Models/SyncOrderRequest.cs`)
- **NO agregar** `CashRegisterSessionId`. El frontend no conoce ni elige la sesión; es responsabilidad del backend resolverla por branch.

### 4.3 CashRegisterSession (`POS.Domain/Models/CashRegisterSession.cs`)
- **AGREGAR** navegación inversa `ICollection<Order>? Orders` (opcional, para queries).
- Sin otros cambios.

---

## 5. Cambios Requeridos por Capa

### 5.1 POS.Domain (Capa de Dominio)

| Archivo | Cambio | Detalles |
|---------|--------|----------|
| `Models/Order.cs` | **MODIFICAR** | Agregar `int? CashRegisterSessionId` + navegación `CashRegisterSession?` |
| `Models/CashRegisterSession.cs` | **MODIFICAR** | Agregar navegación `ICollection<Order>? Orders` |

### 5.2 POS.Repository (Capa de Datos)

| Archivo | Cambio | Detalles |
|---------|--------|----------|
| `ApplicationDbContext.cs` | **MODIFICAR** | Agregar configuración FK en `Order entity`: `HasOne(o => o.CashRegisterSession).WithMany(s => s.Orders).HasForeignKey(o => o.CashRegisterSessionId).IsRequired(false)` |
| `Migrations/` | **CREAR** | Nueva migración `AddCashRegisterSessionIdToOrder` |

**Índice:** Agregar índice en `CashRegisterSessionId` para optimizar queries de ventas por sesión.

**Nota FK nullable:** `IsRequired(false)` porque órdenes de delivery (`OrderSource != Direct`) no tendrán sesión. También, órdenes históricas ya existentes tendrán `null`.

### 5.3 POS.Services (Capa de Servicios)

| Archivo | Cambio | Detalles |
|---------|--------|----------|
| `IService/IOrderService.cs` | **MODIFICAR** | Cambiar firma de `SyncOrdersAsync` para recibir `int branchId` |
| `Service/OrderService.cs` | **MODIFICAR** | Agregar validación en `SyncOrdersAsync` (nueva Phase 1b) + asignación del `CashRegisterSessionId` en mapping |

### 5.4 POS.API (Capa de Presentación)

| Archivo | Cambio | Detalles |
|---------|--------|----------|
| `Controllers/OrdersController.cs` | **MODIFICAR** | Pasar `BranchId` (del JWT) como argumento a `SyncOrdersAsync` |

El `ExceptionMiddleware` existente ya mapea `ValidationException` → 400. No necesita modificaciones.

---

## 6. Flujo de Lógica de Negocio

### 6.1 Punto de inserción en `SyncOrdersAsync`

La validación debe ocurrir **después de Phase 1** (fetch de órdenes existentes) y **antes de Phase 2** (clasificación), como una nueva **Phase 1b**. Es un gate temprano y fail-fast.

```
Phase 1:  Fetch existing orders (ya existe)
Phase 1b: Validate cash register session (NUEVO)
Phase 2:  Classify inserts vs updates (ya existe)
Phase 2b: Validate Counter→Restaurant table assignments (ya existe)
Phase 3:  Batch persist (ya existe)
...
```

### 6.2 Diagrama de Secuencia

```
Controller                         Service                           Repository
    |                                 |                                  |
    |-- POST /sync (branchId) ------->|                                  |
    |                                 |                                  |
    |                                 |── Phase 1: Fetch existing ──────>|
    |                                 |                                  |
    |                                 |── Phase 1b: Validate session ──>|
    |                                 |   GetOpenSessionAsync(branchId)  |
    |                                 |<── session or null ─────────────|
    |                                 |                                  |
    |                                 |   IF session == null:            |
    |                                 |     throw ValidationException    |
    |                                 |     "CASH_SESSION_REQUIRED"      |
    |                                 |                                  |
    |                                 |   ELSE: cache session.Id         |
    |                                 |                                  |
    |                                 |── Phase 2: Classify ────────────>|
    |                                 |   (assign CashRegisterSessionId  |
    |                                 |    to inserts and updates)       |
    |                                 |                                  |
    |                                 |── Phase 3+: Persist ────────────>|
    |                                 |                                  |
    |<── 200 OK + SyncResult ────────|                                  |
```

### 6.3 Lógica de Phase 1b (Pseudocódigo)

```
1. Llamar _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId)
2. Si session == null → throw ValidationException("CASH_SESSION_REQUIRED: ...")
3. Si session != null → guardar session.Id en variable local openSessionId
4. En Phase 2, para cada order (insert o update):
   - SET order.CashRegisterSessionId = openSessionId
```

### 6.4 Reglas de Validación

| # | Regla | Excepción | Código de error |
|---|-------|-----------|----------------|
| 1 | Debe existir una sesión de caja abierta para el branch | `ValidationException` → 400 | `"CASH_SESSION_REQUIRED"` |

**Nota sobre `Status == Open`:** No necesitamos validar el status explícitamente. `GetOpenSessionAsync` ya filtra por `Status == CashRegisterStatus.Open` (`CashRegisterSessionRepository.cs:16-17`). Si retorna `null`, no hay sesión abierta, punto.

### 6.5 branchId — ¿De dónde sacarlo?

El requisito dice: "branchId MUST ALWAYS come from the JWT token." Sin embargo, `SyncOrdersAsync` actualmente recibe `branchId` dentro de cada `SyncOrderRequest.BranchId` — no como parámetro del JWT.

**Análisis del controller actual** (`OrdersController.cs:32-37`):

```
public async Task<IActionResult> Sync([FromBody] List<SyncOrderRequest> orders)
{
    var result = await _orderService.SyncOrdersAsync(orders);
    return Ok(result);
}
```

El `BranchId` del JWT está disponible vía `BaseApiController.BranchId` pero **no se pasa** al service. Las opciones son:

**Opción A:** Pasar `BranchId` desde el controller al service (cambio de firma).
**Opción B:** Usar `requests[0].BranchId` dentro del service (ya disponible).

**Recomendación: Opción A.** Es más correcto arquitecturalmente y consistente con cómo otros endpoints usan `BranchId` desde el JWT. Esto requiere:

1. Cambiar la firma de `IOrderService.SyncOrdersAsync` para recibir `int branchId`.
2. Actualizar el controller para pasar `BranchId`.
3. Usar el `branchId` del JWT para la validación de sesión de caja.

**Impacto adicional de Opción A:** Se podría también validar que los `request.BranchId` del body coincidan con el JWT `branchId`, pero eso es scope de otro feature de seguridad. Para este documento, solo lo usamos para la validación de sesión.

---

## 7. Detalle de Cambios por Archivo

### 7.1 `Order.cs` — Nuevas propiedades

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `CashRegisterSessionId` | `int?` | FK nullable a CashRegisterSession. Null para órdenes de delivery y órdenes históricas pre-migration. |
| `CashRegisterSession` | `CashRegisterSession?` | Navegación virtual. |

### 7.2 `CashRegisterSession.cs` — Nueva navegación

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `Orders` | `ICollection<Order>?` | Navegación inversa. Permite `session.Orders` para queries. |

### 7.3 `ApplicationDbContext.cs` — Configuración FK

Dentro del bloque `modelBuilder.Entity<Order>`:
- Agregar `HasOne(o => o.CashRegisterSession).WithMany(s => s.Orders).HasForeignKey(o => o.CashRegisterSessionId).IsRequired(false).OnDelete(DeleteBehavior.SetNull)`
- Agregar índice: `HasIndex(o => o.CashRegisterSessionId)`

**`OnDelete(DeleteBehavior.SetNull)`**: Si una sesión se elimina (improbable pero defensivo), las órdenes no se eliminan en cascada, solo pierden la referencia.

### 7.4 `IOrderService.cs` — Cambio de firma

**Firma actual:**

```
Task<SyncResult> SyncOrdersAsync(IEnumerable<SyncOrderRequest> orders)
```

**Firma nueva:**

```
Task<SyncResult> SyncOrdersAsync(IEnumerable<SyncOrderRequest> orders, int branchId)
```

### 7.5 `OrdersController.cs` — Pasar branchId

El action method `Sync` debe pasar `BranchId` (del JWT) como segundo argumento a `SyncOrdersAsync`.

### 7.6 `OrderService.SyncOrdersAsync` — Phase 1b + asignación

**Phase 1b (nuevo bloque después de Phase 1):**
1. Llamar `_unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId)`
2. Si `null` → `throw new ValidationException("CASH_SESSION_REQUIRED: No open cash register session found. Open a session before syncing orders.")`
3. Si existe → capturar `var openSessionId = session.Id`

**En Phase 2 — para INSERTS (nuevo order):**
- Después de `MapToOrder(request)`, asignar `order.CashRegisterSessionId = openSessionId`

**En Phase 2 — para UPDATES (existing order):**
- Si `existingOrder.CashRegisterSessionId == null` (orden preexistente sin sesión), asignar `existingOrder.CashRegisterSessionId = openSessionId`
- Si ya tiene un `CashRegisterSessionId`, **no sobreescribirlo** (la orden ya fue vinculada a su sesión original)

### 7.7 `MapToOrder` — Nuevo campo

El método privado `MapToOrder(SyncOrderRequest)` no necesita cambio. El `CashRegisterSessionId` se asigna **después** del mapping, en Phase 2, porque viene de la sesión del backend, no del request.

---

## 8. Migration

### Comando:

```bash
dotnet ef migrations add AddCashRegisterSessionIdToOrder --project POS.Repository --startup-project POS.API
```

### Columna esperada:
- `CashRegisterSessionId` → `integer`, **nullable**, FK a `CashRegisterSessions.Id`
- `ON DELETE SET NULL`
- Índice en `CashRegisterSessionId`

### Datos existentes:
Todas las órdenes existentes tendrán `CashRegisterSessionId = NULL`. Esto es correcto — son órdenes históricas previas a esta feature.

---

## 9. Impacto en `CalculateCashSalesAsync`

Actualmente, `CashRegisterService.CloseSessionAsync` calcula ventas de cash retrospectivamente por ventana temporal (`CashRegisterService.cs:87`). Con el nuevo FK, **opcionalmente** se podría refactorizar a:

```
SELECT SUM(p.AmountCents) FROM OrderPayments p
JOIN Orders o ON p.OrderId = o.Id
WHERE o.CashRegisterSessionId = @sessionId AND p.Method = 'Cash'
```

Sin embargo, esto es un **refactor independiente** que no forma parte de este scope. El cálculo temporal existente seguirá funcionando correctamente porque:
1. Las órdenes nuevas se crean dentro de la ventana temporal de la sesión abierta.
2. El nuevo `CashRegisterSessionId` es un bonus para futura auditoría, no un reemplazo del cálculo.

---

## 10. Error Response Format

Cuando la validación falla, el `ExceptionMiddleware` retorna:

```json
{
  "error": "ValidationError",
  "message": "CASH_SESSION_REQUIRED: No open cash register session found. Open a session before syncing orders.",
  "statusCode": 400
}
```

**El frontend debe detectar la presencia de `CASH_SESSION_REQUIRED`** en el campo `message` para mostrar UI específica (e.g., diálogo "Abrir caja antes de sincronizar").

**Alternativa considerada:** Agregar un campo `code` al error response del middleware. Descartada porque requiere cambiar la estructura global del `ExceptionMiddleware` — scope separado.

---

## 11. Escenarios Edge Case

| Escenario | Comportamiento esperado |
|-----------|------------------------|
| Sync con sesión abierta | Todas las órdenes se vinculan a la sesión. Flujo normal. |
| Sync sin sesión abierta | `ValidationException` 400 — **todo el batch falla**. No se persisten órdenes parciales. |
| Sync, sesión se cierra durante procesamiento | La sesión ya fue leída en Phase 1b. Phase 3 persiste con el `sessionId` original. El xmin en `CashRegisterSession` protege a `CloseSessionAsync` de calcular erróneamente. |
| Orden de delivery (via `/api/delivery/ingest`) | No pasa por `SyncOrdersAsync`. No afectada. `CashRegisterSessionId` queda `null`. |
| Orden existente actualizada (sync update) | Si ya tiene `CashRegisterSessionId`, se preserva. Si es `null` (pre-migration), se asigna la sesión actual. |
| Batch mixto: múltiples branchIds en el body | Validación usa `branchId` del JWT. Si los `request.BranchId` difieren, eso es un problema preexistente fuera de scope. |

---

## 12. Resumen de Archivos a Crear/Modificar

| Archivo | Acción | LOC estimado |
|---------|--------|-------------|
| `POS.Domain/Models/Order.cs` | **MODIFICAR** | +3 |
| `POS.Domain/Models/CashRegisterSession.cs` | **MODIFICAR** | +2 |
| `POS.Repository/ApplicationDbContext.cs` | **MODIFICAR** | +6 |
| `POS.Repository/Migrations/...` | **CREAR** | auto-generada |
| `POS.Services/IService/IOrderService.cs` | **MODIFICAR** | +1 (firma) |
| `POS.Services/Service/OrderService.cs` | **MODIFICAR** | +15 (Phase 1b + asignaciones) |
| `POS.API/Controllers/OrdersController.cs` | **MODIFICAR** | +1 (pasar BranchId) |

**Total:** 0 archivos nuevos (excluyendo migration), 6 archivos modificados. ~28 líneas netas.

---

## 13. Lo que NO está en scope

- Refactorizar `CalculateCashSalesAsync` para usar FK en vez de ventana temporal.
- Validar que `request.BranchId` coincida con el JWT `branchId`.
- Agregar un campo `code` estructurado al `ExceptionMiddleware`.
- Validar sesión de caja en otros endpoints (payments, kitchen status, etc.).
- Retroactivamente asignar `CashRegisterSessionId` a órdenes históricas.
