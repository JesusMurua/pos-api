# Backend Design Document: Extensible Payment Provider Architecture (Clip + MercadoPago)

**Fecha:** 2026-04-03
**Fase:** 14 — Payment Integration
**Autor:** Arquitecto Senior .NET
**Estado:** Pendiente de aprobación

---

## 1. Resumen Ejecutivo

El sistema actual soporta pagos manuales (Cash, Card, Transfer, Other) registrados directamente por el POS frontend durante el sync, y Stripe exclusivamente para **suscripciones** del negocio (no para pagos de clientes en punto de venta). No existe infraestructura para proveedores de pago externos a nivel de transacciones de venta.

Este documento diseña una arquitectura extensible para integrar **Clip** (terminal físico) y **MercadoPago** (QR/Checkout) como proveedores de pago para transacciones de clientes, preservando la compatibilidad offline-first del Sync Engine y la trazabilidad financiera con `CashRegisterSessionId`.

---

## 2. Hallazgos Clave del Análisis

### 2.1 Stripe es SOLO para suscripciones — no para pagos de venta

Stripe está integrado exclusivamente como procesador de suscripciones del negocio:
- `StripeService` → crea Checkout Sessions para planes (Basico, Pro, Enterprise)
- `StripeWebhookController` → recibe eventos de suscripción (`checkout.session.completed`, `invoice.payment_failed`, etc.)
- `StripeEventProcessorWorker` → procesa eventos en background, actualiza `Subscription`
- `StripeEventInbox` → patrón Transactional Outbox para procesamiento idempotente

**No existe ningún vínculo entre Stripe y `OrderPayment`.** Son sistemas completamente disjuntos.

### 2.2 Estado actual de OrderPayment

**Modelo** (`OrderPayment.cs`):
```
Id (int PK), OrderId (string FK), Method (enum: Cash|Card|Transfer|Other),
AmountCents (int), Reference (string? max 50), CreatedAt (DateTime)
```

**Limitaciones críticas:**
- `Reference` es un campo genérico de 50 chars — no puede almacenar IDs de transacción de proveedores, metadata, ni estado de confirmación.
- `PaymentMethod` es un enum cerrado de 4 valores — no distingue entre "Card" manual y "Card vía Clip".
- No hay concepto de `PaymentStatus` — todo pago se asume como completado al momento de registro.
- No hay `ExternalTransactionId` — imposible rastrear la transacción en el proveedor externo.
- No hay `PaymentMetadata` — no se pueden almacenar datos específicos del proveedor.

### 2.3 Flujo actual de pagos en el Sync Engine

En `SyncOrdersAsync`, los pagos llegan como `SyncPaymentRequest`:
```
Method (string), AmountCents (int), Reference (string? max 50)
```

El mapping (`MapToPayment`) parsea el string a `PaymentMethod` enum (fallback a `Cash`):
```
if (!Enum.TryParse<PaymentMethod>(p.Method, true, out var method))
    method = PaymentMethod.Cash;
```

**Implicación:** El frontend ya envía `Method` como string. Si agregamos nuevos valores al enum (`Clip`, `MercadoPago`), el frontend puede enviarlos inmediatamente sin cambios en la API de sync.

### 2.4 Pagos fuera del Sync Engine

`AddPaymentAsync` permite agregar pagos individualmente a una orden existente. El controller valida el `Method` string contra el enum antes de crear el `OrderPayment`. Este endpoint es relevante para pagos que se registran post-sync (e.g., MercadoPago QR que confirma vía webhook).

### 2.5 Impacto en CashRegisterSession

`CalculateCashSalesAsync` suma **solo pagos con `Method == PaymentMethod.Cash`** dentro de la ventana temporal de la sesión. Los nuevos métodos (Clip, MercadoPago) quedarán excluidos automáticamente de este cálculo, lo cual es correcto — no son cash físico.

---

## 3. Arquitectura Propuesta: Extensión vs. Abstracción

### 3.1 Decisión: NO crear un `IPaymentProvider` abstracto

Un `IPaymentProvider` con métodos como `ChargeAsync()`, `RefundAsync()`, `GetStatusAsync()` sería over-engineering porque:

1. **Clip** es un terminal físico — el cobro ocurre localmente entre la terminal y la tarjeta. La API de Clip (si se usa) solo consulta estado, no inicia cobros.
2. **MercadoPago QR** genera un enlace/QR que el cliente escanea — el cobro ocurre en la app de MercadoPago, y la confirmación llega vía webhook.
3. **Cash** y **Transfer** no tienen proveedor — son confirmación manual.
4. Cada proveedor tiene un contrato completamente diferente — forzarlos en una interfaz común generaría abstracciones vacías.

### 3.2 Decisión: Extender OrderPayment + Servicios especializados por proveedor

**Enfoque:** Enriquecer el modelo `OrderPayment` con campos genéricos que soporten cualquier proveedor, y crear servicios dedicados (no una interfaz abstracta) para cada proveedor que necesite lógica de backend.

```
OrderPayment (extendido)
├── Campos existentes (intactos)
├── + PaymentProvider (string?, nullable)     → "Clip", "MercadoPago", null (manual)
├── + PaymentStatus (string)                  → "Completed", "Pending", "Failed"
├── + ExternalTransactionId (string?)         → ID del proveedor
├── + Metadata (string?, JSON)                → Datos específicos del proveedor
└── + ConfirmedAt (DateTime?)                 → Timestamp de confirmación del proveedor

ClipService (nuevo)
├── ValidateTransactionAsync()                → Consulta API de Clip
└── ProcessWebhookAsync()                     → Si Clip soporta webhooks futuro

MercadoPagoService (nuevo)
├── CreatePaymentPreferenceAsync()            → Genera QR/checkout link
├── ProcessWebhookAsync()                     → Recibe confirmación de pago
└── GetPaymentStatusAsync()                   → Consulta estado

PaymentWebhookController (nuevo)
├── POST /api/payments/mercadopago/webhook    → Webhook de MercadoPago
└── POST /api/payments/clip/webhook           → Webhook de Clip (futuro)
```

---

## 4. Modelos de Dominio — Cambios

### 4.1 PaymentMethod Enum — Extender

**Actual:**
```
Cash (0), Card (1), Transfer (2), Other (3)
```

**Propuesto:**
```
Cash (0), Card (1), Transfer (2), Other (3), Clip (4), MercadoPago (5)
```

**Justificación:** Mantener el enum en lugar de migrar a string porque:
- Ya está configurado como `HasConversion<string>()` en EF Core — se almacena como string en la DB.
- El frontend ya envía el método como string y el backend parsea — los nuevos valores son transparentes.
- Mantiene type-safety en C#.
- `CalculateCashSalesAsync` sigue funcionando sin cambios (filtra por `PaymentMethod.Cash`).

### 4.2 PaymentStatus — Nuevo helper (constantes string)

**Archivo:** `POS.Domain/Helpers/PaymentStatus.cs`

| Constante | Valor | Descripción |
|-----------|-------|-------------|
| `Completed` | `"completed"` | Pago confirmado (manual o por proveedor) |
| `Pending` | `"pending"` | Esperando confirmación del proveedor |
| `Failed` | `"failed"` | Rechazado o expirado por el proveedor |
| `Refunded` | `"refunded"` | Reembolsado por el proveedor |

**Decisión: String constants (no enum)** — Sigue el patrón existente de `CashRegisterStatus` y `CashMovementType`. Más flexible para futuros estados sin migrations.

### 4.3 OrderPayment — Campos nuevos

| Propiedad | Tipo | MaxLength | Default | Descripción |
|-----------|------|-----------|---------|-------------|
| `PaymentProvider` | `string?` | 30 | `null` | Identificador del proveedor: `"Clip"`, `"MercadoPago"`, `null` = manual |
| `PaymentStatus` | `string` | 20 | `"completed"` | Estado del pago. Pagos manuales (Cash, Card, Transfer) siempre `"completed"`. |
| `ExternalTransactionId` | `string?` | 100 | `null` | ID de transacción del proveedor externo. |
| `Metadata` | `string?` | — (text) | `null` | JSON con datos específicos del proveedor. |
| `ConfirmedAt` | `DateTime?` | — | `null` | Timestamp de confirmación del proveedor. `null` para manuales y pending. |

### 4.4 SyncPaymentRequest — Campos nuevos

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `PaymentProvider` | `string?` | Proveedor externo, enviado por el frontend |
| `PaymentStatus` | `string?` | Estado offline — el POS puede enviar `"completed"` para Clip (confirmado localmente) o `"pending"` para MercadoPago |
| `ExternalTransactionId` | `string?` | ID de transacción capturado offline por el POS |

**Nota:** `Metadata` y `ConfirmedAt` NO se agregan a `SyncPaymentRequest` — son responsabilidad del backend al procesar webhooks.

### 4.5 AddPaymentRequest — Campos nuevos

Mismos campos que `SyncPaymentRequest`: `PaymentProvider`, `PaymentStatus`, `ExternalTransactionId`.

---

## 5. Flujo Offline-First: Clip (Terminal Físico)

### 5.1 Flujo del usuario

```
1. Cajero cobra $150 MXN en el POS
2. POS envía cobro al terminal Clip (via Bluetooth/USB)
3. Terminal Clip procesa la tarjeta → Aprobado
4. Terminal devuelve Transaction ID al POS
5. POS guarda el pago en IndexedDB:
   { method: "Clip", amountCents: 15000, paymentProvider: "Clip",
     paymentStatus: "completed", externalTransactionId: "clip_txn_abc123" }
6. POS sincroniza vía POST /api/orders/sync
7. Backend persiste con PaymentStatus = "completed"
```

### 5.2 Flujo del backend en SyncOrdersAsync

```
Phase 2 (MapToPayment):
  - Method = "Clip" → PaymentMethod.Clip
  - PaymentProvider = "Clip"
  - PaymentStatus = "completed" (viene del frontend)
  - ExternalTransactionId = "clip_txn_abc123"
  - ConfirmedAt = null (se puede rellenar posteriormente vía validación)

No se necesita validación adicional en el sync:
  - El cobro YA ocurrió en el terminal físico.
  - El POS tiene el transaction ID como comprobante.
  - Intentar validar contra API de Clip en el sync bloquearía el offline-first.
```

### 5.3 Validación opcional post-sync (ClipService)

Un **background job opcional** (no bloqueante) podría:
1. Buscar pagos con `PaymentProvider = "Clip"` y `ConfirmedAt == null`
2. Consultar la API de Clip para verificar el `ExternalTransactionId`
3. Si confirmado → set `ConfirmedAt = utcnow`
4. Si no encontrado → set `PaymentStatus = "failed"`, alertar al manager

**Esto NO bloquea el sync.** Es reconciliación asíncrona.

---

## 6. Flujo MercadoPago (QR / Checkout Link)

### 6.1 Flujo del usuario — Escenario A: QR generado en POS

```
1. Cajero selecciona "Cobrar con MercadoPago" en el POS
2. POS llama a POST /api/payments/mercadopago/create-preference
3. Backend crea Preference en API de MercadoPago → devuelve QR URL
4. POS muestra QR al cliente
5. Cliente escanea y paga en la app de MercadoPago
6. MercadoPago envía webhook a POST /api/payments/mercadopago/webhook
7. Backend actualiza OrderPayment: PaymentStatus = "completed", ConfirmedAt = utcnow
8. POS recibe actualización vía pull/push
```

### 6.2 Flujo del usuario — Escenario B: QR punto de venta (offline-ish)

```
1. Cajero registra el pago como MercadoPago en el POS (sin integración API)
2. Cliente paga vía QR estático del negocio
3. POS guarda: { method: "MercadoPago", status: "pending", amountCents: 15000 }
4. Sync sube el pago con status "pending"
5. MercadoPago webhook confirma → backend actualiza a "completed"
```

### 6.3 Webhook MercadoPago — Diseño

**Patrón:** Reusar el patrón Transactional Inbox probado con Stripe.

**Nuevo modelo: `PaymentWebhookInbox`**

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `Id` | `int` | PK |
| `Provider` | `string` (30) | `"MercadoPago"`, `"Clip"` |
| `ExternalEventId` | `string` (255) | ID del evento del proveedor (idempotency) |
| `EventType` | `string` (100) | Tipo de evento (e.g., `"payment.created"`) |
| `RawJson` | `string` (text) | Payload completo |
| `Status` | `string` (20) | `"pending"`, `"processed"`, `"failed"` |
| `CreatedAt` | `DateTime` | Recepción |
| `ProcessedAt` | `DateTime?` | Procesamiento |
| `ErrorMessage` | `string?` (2000) | Detalle de error |

**Unique constraint** en `(Provider, ExternalEventId)` para idempotencia.

**Justificación de un inbox separado vs. reusar StripeEventInbox:** `StripeEventInbox` está fuertemente acoplado a Stripe (campo `StripeEventId`, procesador que usa Stripe SDK, events de suscripción). Un inbox genérico `PaymentWebhookInbox` sirve para cualquier proveedor de pagos de venta sin contaminar el flujo de suscripciones.

### 6.4 Background Worker: `PaymentWebhookProcessorWorker`

Reusar el patrón de `StripeEventProcessorWorker`:
- Poll cada 5 segundos
- Procesar eventos pending en lotes
- Despachar por `Provider`:
  - `"MercadoPago"` → `MercadoPagoService.ProcessWebhookEventAsync()`
  - `"Clip"` → `ClipService.ProcessWebhookEventAsync()` (futuro)
- Marcar como Processed/Failed

---

## 7. Servicios Especializados por Proveedor

### 7.1 IMercadoPagoService

| Método | Descripción |
|--------|-------------|
| `CreatePaymentPreferenceAsync(string orderId, int amountCents, int branchId)` | Crea Preference en API de MercadoPago. Retorna URL del QR/checkout. |
| `ProcessWebhookEventAsync(PaymentWebhookInbox event)` | Procesa evento de webhook. Busca el `OrderPayment` por `ExternalTransactionId`, actualiza `PaymentStatus` y `ConfirmedAt`. |
| `GetPaymentStatusAsync(string externalTransactionId)` | Consulta estado de pago en API de MercadoPago. Para reconciliación manual. |

### 7.2 IClipService

| Método | Descripción |
|--------|-------------|
| `ValidateTransactionAsync(string externalTransactionId)` | Consulta API de Clip para verificar una transacción. Retorna estado y metadata. |
| `ProcessWebhookEventAsync(PaymentWebhookInbox event)` | Futuro: procesa webhook de Clip si implementan notificaciones. |

### 7.3 Configuración por Proveedor

**Nuevo modelo: `BranchPaymentConfig`**

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `Id` | `int` | PK |
| `BranchId` | `int` | FK a Branch |
| `Provider` | `string` (30) | `"Clip"`, `"MercadoPago"` |
| `IsActive` | `bool` | Habilitado para este branch |
| `ApiKeyEncrypted` | `string?` | API key encriptada vía `IDataProtector` |
| `MerchantId` | `string?` | ID de comercio en el proveedor |
| `WebhookSecret` | `string?` | Secret para validar webhooks |
| `CreatedAt` | `DateTime` | Creación |
| `UpdatedAt` | `DateTime?` | Última actualización |

**Sigue el patrón de `BranchDeliveryConfig`** — una configuración por proveedor por branch.

**Unique constraint** en `(BranchId, Provider)`.

### 7.4 Settings globales

**Nuevo:** `MercadoPagoSettings.cs` y `ClipSettings.cs` en `POS.Domain/Settings/`

Patrón idéntico a `StripeSettings`:
```
MercadoPagoSettings { AccessToken, PublicKey, WebhookSecret }
ClipSettings { ApiKey, WebhookSecret }
```

Registrados vía `IOptions<T>` en Program.cs.

---

## 8. Webhook Controller

### 8.1 `PaymentWebhookController`

| Endpoint | Método | Auth | Descripción |
|----------|--------|------|-------------|
| `POST /api/payments/mercadopago/webhook` | POST | `[AllowAnonymous]` | Recibe notificaciones de MercadoPago |
| `POST /api/payments/clip/webhook` | POST | `[AllowAnonymous]` | Recibe notificaciones de Clip (futuro) |

**Patrón:** Idéntico a `StripeWebhookController`:
1. Leer body raw
2. Validar firma/signature del proveedor
3. Insertar en `PaymentWebhookInbox`
4. Retornar 200 OK inmediatamente
5. Procesamiento real en `PaymentWebhookProcessorWorker`

### 8.2 Endpoint para generar QR/Checkout

| Endpoint | Método | Auth | Descripción |
|----------|--------|------|-------------|
| `POST /api/payments/mercadopago/create-preference` | POST | `Owner,Manager,Cashier` | Genera Preference de MercadoPago para un monto |

**Request:**
```
{ orderId: string, amountCents: int }
```

**Response:**
```
{ preferenceId: string, initPoint: string (URL del QR/checkout) }
```

---

## 9. Impacto en el Sync Engine

### 9.1 Cambios en `MapToPayment`

El método `MapToPayment` en `OrderService.cs` debe mapear los nuevos campos de `SyncPaymentRequest` a `OrderPayment`:

- `PaymentProvider` → directo
- `PaymentStatus` → si null, default `"completed"` (backward compatible con frontend actual que no envía este campo)
- `ExternalTransactionId` → directo

### 9.2 Cambios en `RecalculatePaymentTotals`

**Decisión crítica:** ¿Los pagos con `PaymentStatus = "pending"` cuentan para `PaidCents`?

**Recomendación: NO.** Solo pagos `"completed"` deben contar:

```
PaidCents = Payments.Where(p => p.PaymentStatus == "completed").Sum(p => p.AmountCents)
```

**Implicación:** Una orden con un pago MercadoPago `"pending"` tendrá `IsPaid = false` hasta que el webhook confirme. Esto es correcto — el dinero no ha llegado.

**Impacto en el frontend:** El POS puede mostrar "Esperando confirmación de MercadoPago" mientras `IsPaid == false` y existe un pago pending.

### 9.3 Backward Compatibility

- Pagos existentes sin `PaymentStatus` → la migration asigna default `"completed"`.
- Pagos existentes sin `PaymentProvider` → quedan `null` (manual).
- `SyncPaymentRequest` sin los nuevos campos → defaults: `PaymentStatus = null` → `"completed"`, `PaymentProvider = null`, `ExternalTransactionId = null`.
- **Zero breaking changes** para frontends que no envíen los campos nuevos.

---

## 10. Impacto en CashRegisterSession

### 10.1 `CalculateCashSalesAsync` — Sin cambios

```
.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.AmountCents)
```

Clip y MercadoPago usan `PaymentMethod.Clip` y `PaymentMethod.MercadoPago` respectivamente — quedan excluidos automáticamente. Correcto: no son cash físico.

### 10.2 CashRegisterSessionId — Trazabilidad preservada

Toda orden sincronizada vía `POST /api/orders/sync` ya requiere `CashRegisterSessionId` (Phase 14 anterior). Un pago Clip o MercadoPago en una orden sincronizada hereda la sesión de caja de la orden. La trazabilidad es:

```
CashRegisterSession → Order → OrderPayment (con provider + transaction ID)
```

Esto permite reportes como: "En la sesión de caja #47, se procesaron $5,200 MXN vía Clip en 12 transacciones".

### 10.3 Pagos MercadoPago pendientes y cierre de sesión

Si una sesión de caja se cierra mientras hay pagos MercadoPago `"pending"`:
- `CalculateCashSalesAsync` no los cuenta (no son Cash).
- El webhook llega después y actualiza `PaymentStatus = "completed"` en el `OrderPayment`.
- La orden queda marcada `IsPaid = true`.
- La sesión de caja **no se re-calcula** — el pago MercadoPago no es cash y no afecta el conteo de dinero físico.

---

## 11. Cambios Requeridos por Capa

### 11.1 POS.Domain

| Archivo | Acción | Detalles |
|---------|--------|----------|
| `Enums/PaymentMethod.cs` | **MODIFICAR** | Agregar `Clip = 4`, `MercadoPago = 5` |
| `Helpers/PaymentStatus.cs` | **CREAR** | Constantes: `Completed`, `Pending`, `Failed`, `Refunded` |
| `Models/OrderPayment.cs` | **MODIFICAR** | +5 propiedades: `PaymentProvider`, `PaymentStatus`, `ExternalTransactionId`, `Metadata`, `ConfirmedAt` |
| `Models/SyncOrderRequest.cs` | **MODIFICAR** | +3 propiedades en `SyncPaymentRequest`: `PaymentProvider`, `PaymentStatus`, `ExternalTransactionId` |
| `Models/PaymentWebhookInbox.cs` | **CREAR** | Modelo inbox para webhooks de proveedores de pago |
| `Models/BranchPaymentConfig.cs` | **CREAR** | Configuración de proveedor por branch |
| `Settings/MercadoPagoSettings.cs` | **CREAR** | `AccessToken`, `PublicKey`, `WebhookSecret` |
| `Settings/ClipSettings.cs` | **CREAR** | `ApiKey`, `WebhookSecret` |

### 11.2 POS.Repository

| Archivo | Acción | Detalles |
|---------|--------|----------|
| `ApplicationDbContext.cs` | **MODIFICAR** | Configuración de nuevos campos en `OrderPayment`, nueva entidad `PaymentWebhookInbox`, nueva entidad `BranchPaymentConfig` |
| `IRepository/IPaymentWebhookInboxRepository.cs` | **CREAR** | `GetPendingEventsAsync(int batchSize)` |
| `Repository/PaymentWebhookInboxRepository.cs` | **CREAR** | Implementación |
| `IRepository/IBranchPaymentConfigRepository.cs` | **CREAR** | `GetByBranchAndProviderAsync(int branchId, string provider)` |
| `Repository/BranchPaymentConfigRepository.cs` | **CREAR** | Implementación |
| `IUnitOfWork.cs` | **MODIFICAR** | Agregar `IPaymentWebhookInboxRepository` y `IBranchPaymentConfigRepository` |
| `UnitOfWork.cs` | **MODIFICAR** | Lazy init de nuevos repos |
| `Migrations/` | **CREAR** | Migration para todos los cambios de schema |

### 11.3 POS.Services

| Archivo | Acción | Detalles |
|---------|--------|----------|
| `IService/IMercadoPagoService.cs` | **CREAR** | Interface con `CreatePaymentPreferenceAsync`, `ProcessWebhookEventAsync`, `GetPaymentStatusAsync` |
| `Service/MercadoPagoService.cs` | **CREAR** | Implementación |
| `IService/IClipService.cs` | **CREAR** | Interface con `ValidateTransactionAsync` |
| `Service/ClipService.cs` | **CREAR** | Implementación (stub inicial) |
| `Service/OrderService.cs` | **MODIFICAR** | `MapToPayment`: mapear nuevos campos. `RecalculatePaymentTotals`: filtrar por `PaymentStatus == "completed"` |

### 11.4 POS.API

| Archivo | Acción | Detalles |
|---------|--------|----------|
| `Controllers/PaymentWebhookController.cs` | **CREAR** | Endpoints webhook para MercadoPago y Clip |
| `Controllers/PaymentController.cs` | **CREAR** | Endpoint `create-preference` para MercadoPago |
| `Workers/PaymentWebhookProcessorWorker.cs` | **CREAR** | Background worker que procesa `PaymentWebhookInbox` |
| `Controllers/OrdersController.cs` | **MODIFICAR** | `AddPaymentRequest`: agregar campos `PaymentProvider`, `PaymentStatus`, `ExternalTransactionId` |
| `Program.cs` | **MODIFICAR** | Registrar nuevos services, settings, worker |

---

## 12. Schema: Tablas y Migrations

### 12.1 OrderPayment — Columnas nuevas

| Columna | Tipo SQL | Nullable | Default | Índice |
|---------|----------|----------|---------|--------|
| `PaymentProvider` | `varchar(30)` | YES | `null` | NO |
| `PaymentStatus` | `varchar(20)` | NO | `'completed'` | YES (filtrado) |
| `ExternalTransactionId` | `varchar(100)` | YES | `null` | YES |
| `Metadata` | `text` | YES | `null` | NO |
| `ConfirmedAt` | `timestamp` | YES | `null` | NO |

**Índice en `ExternalTransactionId`:** Para lookup rápido cuando llega un webhook con el transaction ID.

**Índice filtrado en `PaymentStatus`:** `WHERE PaymentStatus = 'pending'` — para el background worker que busca pagos pendientes de reconciliación.

### 12.2 PaymentWebhookInbox — Tabla nueva

| Columna | Tipo SQL | Nullable | Índice |
|---------|----------|----------|--------|
| `Id` | `serial` | NO (PK) | — |
| `Provider` | `varchar(30)` | NO | — |
| `ExternalEventId` | `varchar(255)` | NO | UNIQUE(Provider, ExternalEventId) |
| `EventType` | `varchar(100)` | NO | — |
| `RawJson` | `text` | NO | — |
| `Status` | `varchar(20)` | NO | YES |
| `CreatedAt` | `timestamp` | NO | — |
| `ProcessedAt` | `timestamp` | YES | — |
| `ErrorMessage` | `varchar(2000)` | YES | — |

### 12.3 BranchPaymentConfig — Tabla nueva

| Columna | Tipo SQL | Nullable | Índice |
|---------|----------|----------|--------|
| `Id` | `serial` | NO (PK) | — |
| `BranchId` | `integer` | NO (FK) | UNIQUE(BranchId, Provider) |
| `Provider` | `varchar(30)` | NO | — |
| `IsActive` | `boolean` | NO | — |
| `ApiKeyEncrypted` | `text` | YES | — |
| `MerchantId` | `varchar(100)` | YES | — |
| `WebhookSecret` | `varchar(255)` | YES | — |
| `CreatedAt` | `timestamp` | NO | — |
| `UpdatedAt` | `timestamp` | YES | — |

### 12.4 Datos existentes — Backward Compatibility

- `OrderPayment.PaymentStatus` default `'completed'` → todas las filas existentes quedan como "completed".
- `OrderPayment.PaymentProvider` default `null` → todas las filas existentes quedan como manual.
- **Zero data corruption. Zero breaking changes.**

---

## 13. Impacto en DTOs Existentes

### 13.1 OrderPullPaymentDto — Extender

Actualmente solo tiene `Method` y `AmountCents`. Agregar:

| Propiedad | Tipo |
|-----------|------|
| `PaymentProvider` | `string?` |
| `PaymentStatus` | `string` |
| `ExternalTransactionId` | `string?` |

Esto permite al frontend saber si un pago está pendiente de confirmación.

### 13.2 AddPaymentRequest — Extender

Agregar los mismos 3 campos que `SyncPaymentRequest` extendido.

---

## 14. Escenarios Edge Case

| Escenario | Comportamiento |
|-----------|---------------|
| Sync con pago Clip + CashRegisterSession | Pago se registra como `completed` con `CashRegisterSessionId` de la orden. Trazabilidad completa. |
| Sync con pago MercadoPago pending | Pago registrado como `pending`. `IsPaid = false` (no cuenta en PaidCents). Webhook futuro actualiza a `completed`. |
| Webhook MercadoPago llega antes del sync | `OrderPayment` no existe aún. Webhook se almacena en inbox. Worker busca OrderPayment por `ExternalTransactionId` — no lo encuentra → reintento con backoff. Cuando sync llegue y cree el payment, el siguiente ciclo del worker lo matcheará. |
| Webhook duplicado | `UNIQUE(Provider, ExternalEventId)` en `PaymentWebhookInbox` → `DbUpdateException` → silently ignored. |
| Pago Clip sin ExternalTransactionId | Frontend debe enviar el ID. Si es null, validación falla: `PaymentProvider = "Clip"` requiere `ExternalTransactionId`. |
| Frontend antiguo sin campos nuevos | Backward compatible: `PaymentProvider = null`, `PaymentStatus = null → "completed"`. Flujo actual intacto. |
| Cierre de caja con pagos MercadoPago pending | No afecta: `CalculateCashSalesAsync` solo cuenta `Cash`. Los pagos pending se resuelven independientemente. |
| Refund de pago MercadoPago | Webhook de refund → Worker actualiza `PaymentStatus = "refunded"`. `RecalculatePaymentTotals` excluye refunded de `PaidCents`. `IsPaid` se recalcula. |

---

## 15. Orden de Implementación Sugerido

| Fase | Descripción | Dependencias |
|------|-------------|--------------|
| **14a** | Extender `OrderPayment` + `PaymentMethod` enum + `SyncPaymentRequest` + migration + `MapToPayment` + `RecalculatePaymentTotals` | Ninguna |
| **14b** | `PaymentWebhookInbox` + repo + `BranchPaymentConfig` + repo + UnitOfWork + migration | 14a |
| **14c** | `MercadoPagoService` + `PaymentWebhookController` + `PaymentController` + `PaymentWebhookProcessorWorker` | 14a, 14b |
| **14d** | `ClipService` (validación post-sync) + endpoint de reconciliación | 14a |
| **14e** | Extender `OrderPullPaymentDto` + `AddPaymentRequest` en controller | 14a |

**Phase 14a es el foundation y se puede implementar independientemente** — habilita al frontend a enviar `Clip` y `MercadoPago` como métodos de pago via sync sin romper nada.

---

## 16. Lo que NO está en scope

- Integración con APIs reales de Clip o MercadoPago (requiere credenciales de producción).
- UI del frontend para seleccionar proveedores de pago.
- Refunds automáticos (la lógica de refund es manual inicialmente).
- Reportes de ventas por proveedor de pago (feature de reporting separado).
- Migrar `StripeEventInbox` al inbox genérico (Stripe sigue independiente para suscripciones).
