# AUDIT-007: Gap Analysis — Payments & Smart Terminals (Phase 14)

**Fecha:** 2026-04-06
**Auditor:** Claude Code
**Documentos de referencia:** `docs/BDD-003-design-payment-providers.md`, `.claude/dotnet-api-standards.md`

---

## 1. Estado Actual — Lo que YA ESTA IMPLEMENTADO

### 1.1 PaymentMethod Enum — Proveedores ya existen

| Valor | Estado |
|-------|--------|
| `Cash` (0) | IMPLEMENTADO |
| `Card` (1) | IMPLEMENTADO |
| `Transfer` (2) | IMPLEMENTADO |
| `Other` (3) | IMPLEMENTADO |
| `Clip` (4) | IMPLEMENTADO |
| `MercadoPago` (5) | IMPLEMENTADO |
| `BankTerminal` (6) | IMPLEMENTADO |
| `StoreCredit` (7) | IMPLEMENTADO |
| `LoyaltyPoints` (8) | IMPLEMENTADO |

**Ubicacion:** `POS.Domain/Enums/PaymentMethod.cs`

El enum ya diferencia Clip, MercadoPago y BankTerminal como metodos distintos. Almacenado como string via `HasConversion<string>()`.

---

### 1.2 OrderPayment — Campos de proveedor ya existen

| Campo | Tipo | Estado | Ubicacion |
|-------|------|--------|-----------|
| `PaymentProvider` | `string? (max 30)` | IMPLEMENTADO | `OrderPayment.cs:25` |
| `ExternalTransactionId` | `string? (max 100)` | IMPLEMENTADO | `OrderPayment.cs:29` |
| `PaymentMetadata` | `string? (JSON)` | IMPLEMENTADO | `OrderPayment.cs:33` |
| `OperationId` | `string? (max 100)` | IMPLEMENTADO | `OrderPayment.cs:37` |

**Indices en DB:** `ExternalTransactionId` tiene indice para lookup rapido (webhooks).

---

### 1.3 SyncPaymentRequest — Campos de proveedor ya existen

| Campo | Estado | Ubicacion |
|-------|--------|-----------|
| `PaymentProvider` | IMPLEMENTADO | `SyncOrderRequest.cs:87` |
| `ExternalTransactionId` | IMPLEMENTADO | `SyncOrderRequest.cs:91` |
| `PaymentMetadata` | IMPLEMENTADO | `SyncOrderRequest.cs:94` |
| `OperationId` | IMPLEMENTADO | `SyncOrderRequest.cs:98` |

---

### 1.4 AddPaymentRequest — Campos de proveedor ya existen

Todos los campos (`PaymentProvider`, `ExternalTransactionId`, `PaymentMetadata`, `OperationId`) estan en el DTO del controller. `OrdersController.cs` los mapea al crear `OrderPayment`.

---

### 1.5 MapToPayment — Ya mapea todos los campos

`OrderService.cs` lineas 1237-1254: mapea `PaymentProvider`, `ExternalTransactionId`, `PaymentMetadata`, `OperationId` de `SyncPaymentRequest` a `OrderPayment`.

---

### 1.6 SAT Payment Form Mapping — Ya cubre proveedores

`SatPaymentForm.cs` mapea Clip, MercadoPago, BankTerminal a SAT code `"04"` (Tarjeta de credito). Correcto para CFDI.

---

### 1.7 Webhook Infrastructure — Patrones existentes

| Webhook | Estado | Patron |
|---------|--------|--------|
| Stripe (`/api/stripe/webhook`) | IMPLEMENTADO | Inbox transaccional → Background Worker |
| Facturapi (`/api/webhooks/facturapi`) | IMPLEMENTADO | Procesamiento sincrono en el handler |
| Delivery (`/api/delivery/webhook/{source}/{branchId}`) | IMPLEMENTADO | Procesamiento sincrono |

Los patrones de webhook ya estan probados en produccion.

---

### 1.8 Reporting — Ya soporta desglose por proveedor

`OrderRepository.GetSalesByPaymentMethodAsync` agrupa por `Method + PaymentProvider`, permitiendo reportes como "Ventas por Clip: $5,200 en 12 transacciones".

---

## 2. GAPS IDENTIFICADOS

### GAP-001: No existe `PaymentStatus` — Todo pago se asume como completado (CRITICO)

**Problema:** `OrderPayment` no tiene un campo `PaymentStatus`. Cuando el POS registra un pago MercadoPago via QR, se crea con `AmountCents > 0` y se suma inmediatamente a `PaidCents`. No hay forma de representar un pago **pendiente de confirmacion**.

**Impacto:**
- `RecalculatePaymentTotals` (`OrderService.cs:1285-1289`) suma **todos** los pagos sin filtro: `order.PaidCents = order.Payments.Sum(p => p.AmountCents)`.
- Un pago MercadoPago "pendiente" se contabiliza como cobrado. `IsPaid` se marca `true` antes de que el dinero llegue.
- Si el cliente no completa el pago QR, la orden queda como "pagada" permanentemente.

**Campos faltantes en `OrderPayment`:**
- `PaymentStatus` (`string`, max 20, default `"completed"`) — `"completed"`, `"pending"`, `"failed"`, `"refunded"`
- `ConfirmedAt` (`DateTime?`) — timestamp de confirmacion del proveedor

**Campos faltantes en `SyncPaymentRequest`:**
- `PaymentStatus` (`string?`) — el POS puede enviar `"completed"` (Clip local) o `"pending"` (MercadoPago QR)

**Diseño de referencia:** BDD-003 seccion 4.2 y 4.3 especifican estos campos exactamente.

---

### GAP-002: No existe `AuthCode` para terminales manuales (MEDIO)

**Problema:** En el flujo de "Terminal Manual" (BankTerminal), el cajero pasa la tarjeta en una terminal bancaria fisica, recibe un voucher con un **codigo de autorizacion** del banco, y necesita capturarlo en el POS para trazabilidad.

**Estado actual:** No existe un campo `AuthCode` dedicado. Las opciones actuales son:
- Usar `Reference` (max 50) — posible pero semanticamente incorrecto (Reference es generico).
- Usar `PaymentMetadata` (JSON) — funcional pero requiere parsear JSON para un campo critico de auditoria.
- Usar `ExternalTransactionId` — semanticamente incorrecto (no es un ID de transaccion de proveedor externo).

**Recomendacion:** **No crear un campo dedicado `AuthCode`.** En su lugar, estandarizar el uso de `Reference` como el campo para el codigo de autorizacion bancario en pagos `BankTerminal` y `Card`. El campo ya existe (max 50), es suficiente para auth codes (tipicamente 6 digitos), y evita inflacion de schema. Documentar la convencion en el design doc.

**Alternativa considerada y descartada:** Un campo `AuthCode` en `OrderPayment` seria especifico de un solo flujo (terminal manual) y no aplica a Cash, Transfer, Clip, MercadoPago, StoreCredit, ni LoyaltyPoints. Agregar un campo que solo 1 de 9 metodos de pago usa es over-engineering.

---

### GAP-003: `RecalculatePaymentTotals` no filtra por PaymentStatus (CRITICO)

**Ubicacion:** `OrderService.cs:1285-1289`

```csharp
private static void RecalculatePaymentTotals(Order order)
{
    order.PaidCents = order.Payments.Sum(p => p.AmountCents);
    order.ChangeCents = Math.Max(0, order.PaidCents - order.TotalCents);
}
```

**Problema:** Suma todos los pagos incondicionalmente. Con `PaymentStatus`, debe filtrar:

```
PaidCents = Payments.Where(p.PaymentStatus == "completed").Sum(p.AmountCents)
```

**Dependencia:** Requiere GAP-001 implementado primero.

---

### GAP-004: No existe `PaymentWebhookInbox` — Sin infraestructura para webhooks de pago (ALTO)

**Problema:** No hay tabla inbox ni controller para recibir webhooks de Clip o MercadoPago. El patron Transactional Inbox ya esta probado con `StripeEventInbox`, pero no existe equivalente para proveedores de pago de venta.

**Entidad faltante: `PaymentWebhookInbox`**

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `Id` | `int` | PK |
| `Provider` | `string (max 30)` | `"MercadoPago"`, `"Clip"` |
| `ExternalEventId` | `string (max 255)` | ID del evento del proveedor (idempotency) |
| `EventType` | `string (max 100)` | Tipo de evento |
| `RawJson` | `string (text)` | Payload completo |
| `Status` | `string (max 20)` | `"pending"`, `"processed"`, `"failed"` |
| `CreatedAt` | `DateTime` | Recepcion |
| `ProcessedAt` | `DateTime?` | Procesamiento |
| `ErrorMessage` | `string? (max 2000)` | Detalle de error |

**Unique constraint:** `(Provider, ExternalEventId)` para idempotencia.

---

### GAP-005: No existe `PaymentWebhookController` (ALTO)

**Problema:** No hay endpoints para recibir notificaciones de Clip o MercadoPago.

**Endpoints faltantes:**

| Method | Route | Auth | Descripcion |
|--------|-------|------|-------------|
| POST | `/api/payments/mercadopago/webhook` | AllowAnonymous | Webhook de MercadoPago |
| POST | `/api/payments/clip/webhook` | AllowAnonymous | Webhook de Clip (futuro) |

**Patron:** Identico a `StripeWebhookController` — validar firma, insertar en inbox, retornar 200 inmediatamente.

---

### GAP-006: No existe `PaymentWebhookProcessorWorker` (ALTO)

**Problema:** Sin background worker para procesar eventos del inbox. El patron ya existe en `StripeEventProcessorWorker`.

**Responsabilidades:**
1. Poll `PaymentWebhookInbox` cada N segundos
2. Despachar por `Provider` al servicio correspondiente
3. Buscar `OrderPayment` por `ExternalTransactionId`
4. Actualizar `PaymentStatus = "completed"`, `ConfirmedAt = utcnow`
5. Llamar `RecalculatePaymentTotals` en la orden afectada
6. Marcar evento como `Processed` o `Failed`

---

### GAP-007: No existe `BranchPaymentConfig` (MEDIO)

**Problema:** No hay tabla para almacenar credenciales de proveedor por branch (API keys de Clip, access tokens de MercadoPago, webhook secrets).

**Estado actual:** Las credenciales de Stripe y Facturapi se almacenan en `appsettings.json` via `IOptions<T>` — son globales de la plataforma. Para proveedores de pago de venta, cada branch puede tener sus propias credenciales de Clip/MercadoPago.

**Entidad faltante: `BranchPaymentConfig`**

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `Id` | `int` | PK |
| `BranchId` | `int` | FK a Branch |
| `Provider` | `string (max 30)` | `"Clip"`, `"MercadoPago"` |
| `IsActive` | `bool` | Habilitado |
| `ApiKeyEncrypted` | `string?` | API key encriptada |
| `MerchantId` | `string? (max 100)` | ID de comercio |
| `WebhookSecret` | `string? (max 255)` | Secret para validar webhooks |
| `CreatedAt` | `DateTime` | |
| `UpdatedAt` | `DateTime?` | |

**Unique constraint:** `(BranchId, Provider)`.

---

### GAP-008: No existen `MercadoPagoService` ni `ClipService` (ALTO)

**Problema:** No hay servicios especializados para interactuar con las APIs de los proveedores.

**MercadoPagoService necesita:**
- `CreatePaymentPreferenceAsync(orderId, amountCents, branchId)` → genera QR/checkout URL
- `ProcessWebhookEventAsync(PaymentWebhookInbox event)` → procesa confirmacion
- `GetPaymentStatusAsync(externalTransactionId)` → consulta estado

**ClipService necesita:**
- `ValidateTransactionAsync(externalTransactionId)` → verifica transaccion contra API de Clip
- `ProcessWebhookEventAsync(PaymentWebhookInbox event)` → futuro

---

### GAP-009: No existe endpoint para generar QR/Checkout de MercadoPago (MEDIO)

**Endpoint faltante:**

| Method | Route | Auth | Descripcion |
|--------|-------|------|-------------|
| POST | `/api/payments/mercadopago/create-preference` | Owner,Manager,Cashier | Genera Preference y retorna URL del QR |

**Request:** `{ orderId, amountCents }`
**Response:** `{ preferenceId, initPoint (URL) }`

---

### GAP-010: Settings de proveedores no existen (BAJO)

**Archivos faltantes:**
- `POS.Domain/Settings/MercadoPagoSettings.cs` — `AccessToken`, `PublicKey`, `WebhookSecret`
- `POS.Domain/Settings/ClipSettings.cs` — `ApiKey`, `WebhookSecret`

Requiere registro en `Program.cs` via `IOptions<T>`.

---

## 3. Plan de Implementacion Step-by-Step

### Fase 14a: Foundation — PaymentStatus + RecalculatePaymentTotals (CRITICO)

**Objetivo:** Habilitar el concepto de pagos pendientes sin romper backward compatibility.

**Archivos:**
| Archivo | Accion |
|---------|--------|
| `POS.Domain/Helpers/PaymentStatus.cs` | CREAR — constantes string |
| `POS.Domain/Models/OrderPayment.cs` | MODIFICAR — +`PaymentStatus` (string, default "completed"), +`ConfirmedAt` (DateTime?) |
| `POS.Repository/ApplicationDbContext.cs` | MODIFICAR — config nuevos campos, indice filtrado en PaymentStatus |
| `POS.Repository/Migrations/` | CREAR — migration auto-generada |
| `POS.Services/Service/OrderService.cs` | MODIFICAR — `RecalculatePaymentTotals` filtra por status completed, `MapToPayment` mapea nuevo campo |
| `POS.Domain/Models/SyncOrderRequest.cs` | MODIFICAR — +`PaymentStatus` en `SyncPaymentRequest` |

**Backward compat:** Default `"completed"` en migration → todos los pagos existentes siguen contando. Frontends que no envien el campo reciben default "completed".

**LOC estimado:** ~40

---

### Fase 14b: Webhook Infrastructure — Inbox + Worker + Controller

**Objetivo:** Infraestructura generica para recibir y procesar webhooks de proveedores de pago.

**Archivos:**
| Archivo | Accion |
|---------|--------|
| `POS.Domain/Models/PaymentWebhookInbox.cs` | CREAR |
| `POS.Repository/ApplicationDbContext.cs` | MODIFICAR — DbSet + config |
| `POS.Repository/IRepository/IPaymentWebhookInboxRepository.cs` | CREAR |
| `POS.Repository/Repository/PaymentWebhookInboxRepository.cs` | CREAR |
| `POS.Repository/IUnitOfWork.cs` | MODIFICAR |
| `POS.Repository/UnitOfWork.cs` | MODIFICAR |
| `POS.Repository/Migrations/` | CREAR |
| `POS.API/Controllers/PaymentWebhookController.cs` | CREAR |
| `POS.API/Workers/PaymentWebhookProcessorWorker.cs` | CREAR |

**LOC estimado:** ~200

---

### Fase 14c: BranchPaymentConfig + Settings

**Objetivo:** Almacenar credenciales de proveedor por branch.

**Archivos:**
| Archivo | Accion |
|---------|--------|
| `POS.Domain/Models/BranchPaymentConfig.cs` | CREAR |
| `POS.Domain/Settings/MercadoPagoSettings.cs` | CREAR |
| `POS.Domain/Settings/ClipSettings.cs` | CREAR |
| `POS.Repository/ApplicationDbContext.cs` | MODIFICAR |
| `POS.Repository/IRepository/IBranchPaymentConfigRepository.cs` | CREAR |
| `POS.Repository/Repository/BranchPaymentConfigRepository.cs` | CREAR |
| `POS.Repository/IUnitOfWork.cs` | MODIFICAR |
| `POS.Repository/UnitOfWork.cs` | MODIFICAR |
| `POS.Repository/Migrations/` | CREAR |
| `POS.API/Program.cs` | MODIFICAR — registrar IOptions |

**LOC estimado:** ~120

---

### Fase 14d: MercadoPagoService + Endpoint QR

**Objetivo:** Integracion con API de MercadoPago para generar QR y procesar confirmaciones.

**Archivos:**
| Archivo | Accion |
|---------|--------|
| `POS.Services/IService/IMercadoPagoService.cs` | CREAR |
| `POS.Services/Service/MercadoPagoService.cs` | CREAR |
| `POS.API/Controllers/PaymentController.cs` | CREAR — endpoint create-preference |
| `POS.API/Program.cs` | MODIFICAR — registrar service |

**LOC estimado:** ~150

---

### Fase 14e: ClipService (Validacion Post-Sync)

**Objetivo:** Verificacion asincrona de transacciones Clip contra la API.

**Archivos:**
| Archivo | Accion |
|---------|--------|
| `POS.Services/IService/IClipService.cs` | CREAR |
| `POS.Services/Service/ClipService.cs` | CREAR |
| `POS.API/Program.cs` | MODIFICAR — registrar service |

**LOC estimado:** ~80

---

## 4. Resumen Ejecutivo

| # | Gap | Severidad | Fase | LOC |
|---|-----|-----------|------|-----|
| GAP-001 | No existe PaymentStatus en OrderPayment | CRITICO | 14a | ~15 |
| GAP-002 | No existe AuthCode dedicado | MEDIO | N/A (usar Reference) | 0 |
| GAP-003 | RecalculatePaymentTotals no filtra por status | CRITICO | 14a | ~5 |
| GAP-004 | No existe PaymentWebhookInbox | ALTO | 14b | ~60 |
| GAP-005 | No existe PaymentWebhookController | ALTO | 14b | ~70 |
| GAP-006 | No existe PaymentWebhookProcessorWorker | ALTO | 14b | ~70 |
| GAP-007 | No existe BranchPaymentConfig | MEDIO | 14c | ~120 |
| GAP-008 | No existen MercadoPagoService ni ClipService | ALTO | 14d/14e | ~230 |
| GAP-009 | No existe endpoint create-preference | MEDIO | 14d | incluido |
| GAP-010 | No existen Settings de proveedores | BAJO | 14c | incluido |

**Prioridad de implementacion:** 14a (foundation) → 14b (webhook infra) → 14c (config) → 14d (MercadoPago) → 14e (Clip)

**Fase 14a es autocontenida** y habilita al frontend a enviar pagos con `PaymentStatus = "pending"` sin romper nada existente. Se puede implementar independientemente de las demas fases.

---

## 5. Lo que NO esta en scope

- Integracion real con APIs de Clip o MercadoPago (requiere credenciales de produccion).
- UI del frontend para seleccionar proveedores de pago.
- Refunds automaticos.
- Migrar `StripeEventInbox` al inbox generico (Stripe sigue independiente para suscripciones).
- Validacion de que pagos `BankTerminal`/`Card` requieran `Reference` (auth code) — es responsabilidad del frontend.
