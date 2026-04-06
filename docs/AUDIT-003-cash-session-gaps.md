# AUDIT-003: Gap Analysis — Mandatory Cash Register Session

**Fecha:** 2026-04-06
**Auditor:** Claude Code
**Documentos de referencia:** `docs/design-mandatory-cash-session.md`, `.claude/dotnet-api-standards.md`

---

## 1. Estado Actual — Lo que YA ESTÁ IMPLEMENTADO

### 1.1 Modelo `Order` — FK existe y es nullable

| Aspecto | Estado | Ubicación |
|---------|--------|-----------|
| `CashRegisterSessionId` (int?) en `Order.cs` | IMPLEMENTADO | `POS.Domain/Models/Order.cs:80` |
| Navegación virtual `CashRegisterSession?` | IMPLEMENTADO | `Order.cs` |
| Navegación inversa `ICollection<Order> Orders` en `CashRegisterSession` | IMPLEMENTADO | `CashRegisterSession.cs` |
| FK configurada con `IsRequired(false)` + `OnDelete(SetNull)` | IMPLEMENTADO | `ApplicationDbContext.cs` |
| Índice en `CashRegisterSessionId` | IMPLEMENTADO | `ApplicationDbContext.cs` |
| Migration `AddCashRegisterSessionToOrder` | IMPLEMENTADO | `20260403074350` |

**Veredicto:** El design doc (sección 3, Opción B) se implementó completamente a nivel de schema.

---

### 1.2 `SyncOrdersAsync` — Validación Phase 1b existe

| Aspecto | Estado | Ubicación |
|---------|--------|-----------|
| `SyncOrderRequest.CashRegisterSessionId` (int?) | IMPLEMENTADO | `SyncOrderRequest.cs` |
| Validación: all requests must have `CashRegisterSessionId` | IMPLEMENTADO | `OrderService.cs:54-56` |
| Validación: session exists, belongs to branch, status == Open | IMPLEMENTADO | `OrderService.cs:58-69` |
| Error code `CASH_SESSION_REQUIRED` | IMPLEMENTADO | `OrderService.cs:56` |
| Error code `CASH_SESSION_CLOSED` | IMPLEMENTADO | `OrderService.cs:68` |
| Asignación `CashRegisterSessionId` en inserts (MapToOrder) | IMPLEMENTADO | `OrderService.cs:1121` |
| Asignación `CashRegisterSessionId` en updates | IMPLEMENTADO | `OrderService.cs:94` |
| Firma `SyncOrdersAsync(orders, branchId)` | IMPLEMENTADO | `OrderService.cs:41` |

**Veredicto:** El Sync Engine valida correctamente la sesión de caja para órdenes locales.

---

### 1.3 `CashRegisterService` — Método de validación eficiente

| Aspecto | Estado | Ubicación |
|---------|--------|-----------|
| `GetOpenSessionAsync(branchId)` retorna sesión o null | IMPLEMENTADO | `CashRegisterService.cs:25-28` |
| Filtered unique index: 1 sesión abierta por branch | IMPLEMENTADO | `ApplicationDbContext.cs` |
| `CloseSessionAsync` con cálculo financiero + xmin concurrency | IMPLEMENTADO | `CashRegisterService.cs:67-116` |

**Veredicto:** Infraestructura de validación completa.

---

## 2. GAPS IDENTIFICADOS

### GAP-001: `AddPaymentAsync` no valida sesión de caja abierta (CRÍTICO)

**Ubicación:** [OrderService.cs:681-696](POS.Services/Service/OrderService.cs#L681-L696)

**Problema:** Un usuario puede agregar pagos a una orden **después de que la sesión de caja fue cerrada**. Esto rompe la integridad financiera porque:
- `CloseSessionAsync` calcula `CashSalesCents` sumando pagos dentro de la ventana temporal `OpenedAt → ClosedAt`.
- Un pago registrado después del cierre **no aparecerá en ningún cierre de caja**.
- El pago queda en un "vacío financiero" — exactamente el problema que la sesión de caja busca prevenir.

**Validación faltante:**
```
1. Obtener order.CashRegisterSessionId
2. Si es null → (orden de delivery, permitir sin validación)
3. Si no es null → verificar que la sesión asociada siga abierta
   O BIEN → verificar que exista ALGUNA sesión abierta para el branch
```

**Impacto:** Alto. Los pagos parciales y pagos posteriores al sync son un flujo operativo común (e.g., cliente pide cuenta, mesero va a cobrar minutos después).

---

### GAP-002: `SplitOrderAsync` no propaga `CashRegisterSessionId` a órdenes nuevas (CRÍTICO)

**Ubicación:** [OrderService.cs:1015-1030](POS.Services/Service/OrderService.cs#L1015-L1030)

**Problema:** Al dividir una orden, las nuevas órdenes se crean **sin `CashRegisterSessionId`**. El bloque `new Order { ... }` en línea 1015 no incluye esta propiedad. Resultado:
- La orden original conserva su `CashRegisterSessionId`.
- Las órdenes derivadas del split quedan con `CashRegisterSessionId = null`.
- Al cerrar caja, las ventas de las órdenes split no se vinculan a ninguna sesión.

**Fix requerido:** Copiar `source.CashRegisterSessionId` al crear cada nueva orden en el split.

---

### GAP-003: `MergeOrdersAsync` — no valida coherencia de sesión (MEDIO)

**Ubicación:** [OrderService.cs:887-902](POS.Services/Service/OrderService.cs#L887-L902)

**Problema:** Al fusionar dos órdenes, no se verifica que ambas pertenezcan a la misma sesión de caja. Si la orden source tiene `CashRegisterSessionId = 5` y la target tiene `CashRegisterSessionId = 7` (posible si se abrió una nueva sesión entre la creación de ambas), la fusión procede sin advertencia. Resultado:
- Los items de la orden source se mueven a la target.
- Las ventas quedan contabilizadas bajo la sesión de la orden target, pero fueron originadas en otra sesión.

**Riesgo:** Medio. En la práctica es poco probable porque merge ocurre en la misma sesión operativa, pero no está garantizado programáticamente.

---

### GAP-004: `MoveItemsAsync` — misma situación que merge (BAJO)

**Ubicación:** [OrderService.cs:785-800](POS.Services/Service/OrderService.cs#L785-L800)

**Problema:** Al mover items entre órdenes, no se verifica que ambas órdenes compartan el mismo `CashRegisterSessionId`. Impacto similar a GAP-003 pero más granular (solo algunos items se mueven).

**Riesgo:** Bajo. Operacionalmente, mover items entre órdenes de diferentes sesiones es un edge case muy improbable.

---

### GAP-005: `SyncOrderRequest.CashRegisterSessionId` proviene del frontend (DISEÑO)

**Ubicación:** [OrderService.cs:54-69](POS.Services/Service/OrderService.cs#L54-L69)

**Divergencia del design doc:** El documento de diseño (sección 4.2) especifica:

> `SyncOrderRequest` — **NO agregar** `CashRegisterSessionId`. El frontend no conoce ni elige la sesión; es responsabilidad del backend resolverla por branch.

**Estado actual:** `SyncOrderRequest.CashRegisterSessionId` **sí existe** y el frontend lo envía. La validación en Phase 1b verifica que el frontend envíe un sessionId válido en lugar de que el backend lo resuelva automáticamente.

**Implicaciones:**
- El frontend tiene la responsabilidad de conocer y enviar el sessionId correcto.
- Si el frontend envía un sessionId incorrecto (de otra sesión, de otro branch), la validación lo atrapa — pero el error recae en el cliente.
- Esta decisión funciona pero se desvía del principio "backend is source of truth" del design doc.

**Riesgo:** Bajo en la práctica (la validación server-side protege), pero vale la pena documentar la decisión explícitamente.

---

### GAP-006: Updates sobrescriben `CashRegisterSessionId` incondicionalmente (MEDIO)

**Ubicación:** [OrderService.cs:94](POS.Services/Service/OrderService.cs#L94)

**Divergencia del design doc:** El documento (sección 7.6) especifica:

> Para UPDATES: Si ya tiene un `CashRegisterSessionId`, **no sobreescribirlo** (la orden ya fue vinculada a su sesión original).

**Estado actual:** Línea 94 hace `existingOrder.CashRegisterSessionId = request.CashRegisterSessionId` **incondicionalmente**, sin verificar si la orden ya tenía una sesión asignada.

**Impacto:** Si un usuario abre sesión A, crea una orden, cierra sesión A, abre sesión B, y el frontend re-sincroniza la orden (con el sessionId de B), la orden **cambiará de sesión A a sesión B**. Esto corrompe la contabilidad de ambas sesiones.

---

## 3. Plan de Implementación Step-by-Step

### Paso 1: Fix GAP-002 — Propagar `CashRegisterSessionId` en Split (CRÍTICO)

**Archivo:** `POS.Services/Service/OrderService.cs`
**Cambio:** En `SplitOrderAsync`, agregar `CashRegisterSessionId = source.CashRegisterSessionId` al bloque `new Order { ... }` (línea ~1015).
**LOC:** +1
**Riesgo:** Ninguno. Es una copia directa de la propiedad de la orden fuente.

---

### Paso 2: Fix GAP-006 — No sobrescribir sesión en updates (MEDIO)

**Archivo:** `POS.Services/Service/OrderService.cs`
**Cambio:** Línea 94, cambiar de asignación incondicional a condicional:
```
Si existingOrder.CashRegisterSessionId == null → asignar request.CashRegisterSessionId
Si ya tiene valor → preservar el existente
```
**LOC:** +2 (cambio de 1 línea a condición)
**Riesgo:** Bajo. Alinea con el design doc. Órdenes pre-migration (null) se benefician al recibir sesión en el primer update.

---

### Paso 3: Fix GAP-001 — Validar sesión abierta en `AddPaymentAsync` (CRÍTICO)

**Archivo:** `POS.Services/Service/OrderService.cs`
**Cambio:** Antes de agregar el pago, verificar:
1. Si `order.CashRegisterSessionId != null` → la orden es local (Direct), verificar que exista una sesión abierta para el branch.
2. Si `order.CashRegisterSessionId == null` → la orden es de delivery, permitir sin restricción.
**Error:** `ValidationException("CASH_SESSION_REQUIRED: ...")` → 400.
**LOC:** +6
**Riesgo:** Medio. Impacta flujo de cobro. Requiere que el frontend maneje el error y muestre "Abre caja para cobrar".

**Nota importante:** La validación debe ser "existe una sesión abierta para el branch", NO "la sesión original de la orden sigue abierta". Razón: es válido cobrar una orden de la sesión anterior en la sesión actual (el cajero reabrió caja).

---

### Paso 4: Evaluar GAP-003 y GAP-004 — Merge/Move session coherence (BAJO)

**Recomendación:** No bloquear merge/move por diferencia de sesión. En su lugar:
- **Opción A (mínima):** Log warning cuando las sesiones difieren. No bloquear la operación.
- **Opción B (defensiva):** Validar que ambas órdenes compartan sesión. Rechazar si difieren.

**Sugerencia:** Opción A para v1. Monitear si ocurre en producción antes de agregar restricciones.

---

### Paso 5: Documentar GAP-005 — Decisión de diseño sobre sessionId del frontend

**Archivo:** `docs/design-mandatory-cash-session.md`
**Cambio:** Actualizar sección 4.2 para reflejar que se eligió enviar `CashRegisterSessionId` desde el frontend en lugar de resolverlo en el backend, y documentar el razonamiento (e.g., soporte offline donde el frontend cachea el sessionId activo).

---

## 4. Resumen Ejecutivo

| # | Gap | Severidad | LOC estimado | Riesgo de breaking change |
|---|-----|-----------|-------------|--------------------------|
| GAP-001 | `AddPaymentAsync` sin validación de sesión | CRÍTICO | +6 | Medio (flujo de cobro) |
| GAP-002 | `SplitOrderAsync` no propaga sessionId | CRÍTICO | +1 | Ninguno |
| GAP-003 | `MergeOrdersAsync` sin coherencia de sesión | MEDIO | +3 (log) | Ninguno |
| GAP-004 | `MoveItemsAsync` sin coherencia de sesión | BAJO | +3 (log) | Ninguno |
| GAP-005 | SessionId viene del frontend vs design doc | DISEÑO | 0 (doc) | Ninguno |
| GAP-006 | Update sobrescribe sessionId incondicionalmente | MEDIO | +2 | Bajo |

**Prioridad de implementación:** GAP-002 → GAP-006 → GAP-001 → GAP-003/004 → GAP-005
