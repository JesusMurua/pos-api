# BDD-004 Phase 15d — Public Invoicing API for Customer Portal

**Fecha:** 2026-04-03
**Fase:** 15d — Public Self-Invoicing Endpoints
**Autor:** Arquitecto Senior .NET
**Estado:** Pendiente de aprobacion

---

## 1. Resumen Ejecutivo

El landing page de Kaja (Next.js) necesita permitir que los clientes se auto-facturen usando la URL del ticket impreso (e.g., `kaja.mx/factura?orderId=abc-123`). El cliente no esta autenticado — no tiene JWT ni cuenta en el sistema. Se necesitan endpoints publicos con seguridad por validacion de datos del recibo fisico.

---

## 2. Hallazgos del Analisis

### 2.1 Estado actual de InvoicingService

`RequestIndividualInvoiceAsync` recibe `(string orderId, int fiscalCustomerId, int branchId)`. El `branchId` viene del JWT y se usa para validar que la orden pertenece al branch del cajero. El `fiscalCustomerId` asume que el cliente fiscal ya existe en la DB.

**Problemas para el flujo publico:**
1. No hay JWT → no hay `branchId`. Pero el `orderId` ya contiene la relacion `Order.BranchId`.
2. El cliente no tiene un `fiscalCustomerId` previo — necesita enviar los datos fiscales completos en el request.
3. No hay mecanismo de seguridad que pruebe que el solicitante posee el ticket fisico.

### 2.2 Endpoints publicos existentes en el codebase

El codebase ya tiene multiples endpoints `[AllowAnonymous]`:
- `GET /api/branch/public/{id}` — Info basica de sucursal
- `GET /api/products/public` — Productos para kiosk
- `POST /api/stripe/webhook` — Webhook de Stripe

**Patron comun:** Retornan datos no-sensibles o validan via signature. No hay rate limiting especifico por endpoint (solo `RegistrationPolicy` para auth).

### 2.3 Modelo de seguridad — Receipt Proof

En Mexico, el ticket impreso contiene: `OrderNumber`, `Total`, `Fecha`, `Sucursal`. El `orderId` (UUID) no aparece en el ticket impreso — se codifica en un QR o URL corta.

**Amenaza:** Un atacante que obtiene un UUID de orden podria intentar facturar sin tener el ticket. Para mitigar:
- **Requerir `TotalCents`** en el payload — prueba que el solicitante vio el ticket fisico.
- **Rate limiting** para evitar fuerza bruta contra UUIDs.
- **Ventana temporal** — solo se puede facturar dentro del mes natural + el dia 1 del mes siguiente (regla SAT: factura global se emite a mas tardar el dia 17 del mes siguiente).

### 2.4 Business.InvoicingEnabled

Si el negocio no ha habilitado facturacion (`InvoicingEnabled == false`), los endpoints publicos deben retornar un mensaje claro indicando que el negocio no ofrece facturacion electronica.

---

## 3. Decisiones Arquitecturales

### 3.1 Controller separado vs. extender InvoicingController

**Decision: Controller separado `PublicInvoicingController`.**

`InvoicingController` hereda de `BaseApiController` y esta marcado `[Authorize]`. Los endpoints publicos necesitan `[AllowAnonymous]` y no tienen acceso a `BranchId`/`BusinessId` del JWT. Mezclarlos en un solo controller causaria confusiones de seguridad.

### 3.2 FiscalCustomer — crear inline vs. requerir previo

**Decision: Crear inline si no existe.** El endpoint publico recibe los datos fiscales completos (RFC, razon social, regimen, CP, email). El service busca si ya existe un `FiscalCustomer` con ese RFC para el business. Si no, lo crea automaticamente. Esto evita un round-trip adicional GET+POST desde el frontend.

### 3.3 branchId — como resolverlo sin JWT

**Decision: Derivar de la orden.** `Order.BranchId` ya esta en la DB. El service carga la orden, obtiene el `BranchId`, y con el llega al `Business` para validar `InvoicingEnabled` y el RFC emisor.

### 3.4 Rate limiting

**Decision: Nueva policy `PublicInvoicingPolicy`.** 10 requests por minuto por IP. Impide ataques de fuerza bruta contra UUIDs y abuso del endpoint de emision.

### 3.5 Seguridad: Receipt Proof

**Decision: Requerir `TotalCents` como campo obligatorio en el POST.** Si el `TotalCents` del request no coincide con el `Order.TotalCents` de la DB, retornar `400 Bad Request` con mensaje generico (no revelar que el monto es incorrecto para evitar oracle attacks).

---

## 4. API Contract

### 4.1 `GET /api/invoicing/public/{orderId}` — Consultar orden para facturacion

| Aspecto | Valor |
|---------|-------|
| Metodo | `GET` |
| Auth | `[AllowAnonymous]` |
| Rate Limit | `PublicInvoicingPolicy` (10/min por IP) |
| Descripcion | Retorna datos basicos de la orden para el portal de auto-facturacion |

**Response 200 OK — Orden facturables:**
```json
{
  "orderId": "abc-123-def-456",
  "branchName": "Taqueria El Sol - Centro",
  "date": "2026-04-03T14:30:00Z",
  "totalCents": 15000,
  "invoiceStatus": "None",
  "invoicingEnabled": true,
  "canInvoice": true
}
```

**Response 200 OK — Ya facturada:**
```json
{
  "orderId": "abc-123-def-456",
  "branchName": "Taqueria El Sol - Centro",
  "date": "2026-04-03T14:30:00Z",
  "totalCents": 15000,
  "invoiceStatus": "Issued",
  "invoicingEnabled": true,
  "canInvoice": false,
  "invoiceUrl": "https://facturapi.io/invoices/..."
}
```

**Response 200 OK — Negocio sin facturacion:**
```json
{
  "orderId": "abc-123-def-456",
  "branchName": "Taqueria El Sol - Centro",
  "date": "2026-04-03T14:30:00Z",
  "totalCents": 15000,
  "invoiceStatus": "None",
  "invoicingEnabled": false,
  "canInvoice": false
}
```

**Response 404 — Orden no encontrada:**
```json
{
  "error": "NotFound",
  "message": "Order not found",
  "statusCode": 404
}
```

**Datos NO expuestos:** `BusinessId`, `BranchId`, `UserId`, `CashRegisterSessionId`, items, pagos, descuentos, nombre del cajero.

### 4.2 `POST /api/invoicing/public/request` — Solicitar factura

| Aspecto | Valor |
|---------|-------|
| Metodo | `POST` |
| Auth | `[AllowAnonymous]` |
| Rate Limit | `PublicInvoicingPolicy` (10/min por IP) |
| Descripcion | Emite una factura individual para una orden, creando el FiscalCustomer si es necesario |

**Request Body:**
```json
{
  "orderId": "abc-123-def-456",
  "totalCents": 15000,
  "rfc": "XAXX010101000",
  "fiscalName": "Juan Perez Lopez",
  "taxRegime": "612",
  "zipCode": "06600",
  "email": "juan@email.com",
  "cfdiUse": "G03"
}
```

| Campo | Tipo | Required | MaxLength | Descripcion |
|-------|------|----------|-----------|-------------|
| `orderId` | `string` | SI | 36 | UUID de la orden |
| `totalCents` | `int` | SI | — | Monto total del ticket (receipt proof) |
| `rfc` | `string` | SI | 13 | RFC del solicitante |
| `fiscalName` | `string` | SI | 300 | Razon social exacta ante SAT |
| `taxRegime` | `string` | SI | 3 | Clave regimen fiscal SAT |
| `zipCode` | `string` | SI | 5 | Codigo postal fiscal |
| `email` | `string?` | NO | 255 | Email para envio de factura |
| `cfdiUse` | `string?` | NO | 5 | Uso CFDI (default: `"G03"`) |

**Response 200 OK:**
```json
{
  "orderId": "abc-123-def-456",
  "customerRfc": "XAXX010101000",
  "totalCents": 15000,
  "facturapiId": "fpi_ind_abc123...",
  "status": "Pending"
}
```

**Response 400 — Receipt proof fallido:**
```json
{
  "error": "ValidationError",
  "message": "The provided order data does not match our records.",
  "statusCode": 400
}
```

**Response 400 — Ya facturada:**
```json
{
  "error": "ValidationError",
  "message": "This order has already been invoiced.",
  "statusCode": 400
}
```

**Response 400 — Negocio sin facturacion:**
```json
{
  "error": "ValidationError",
  "message": "Electronic invoicing is not enabled for this business.",
  "statusCode": 400
}
```

---

## 5. Flujo de Logica

### 5.1 GET — Consulta de orden

```
1. Recibir orderId del path
2. Buscar orden en DB (incluir Branch → Business)
3. Si no existe → 404
4. Si orden cancelada → responder con canInvoice = false
5. Construir DTO seguro (sin datos sensibles)
6. Determinar canInvoice:
   - Business.InvoicingEnabled == true
   - Order.InvoiceStatus == None
   - Order.CancellationReason == null
   - Dentro de ventana temporal (max 40 dias desde CreatedAt)
7. Si ya facturada y tiene InvoiceUrl → incluir en response
8. Retornar DTO
```

### 5.2 POST — Solicitud de factura

```
1. Recibir payload completo
2. Buscar orden en DB (incluir Branch → Business)
3. Si no existe → 404
4. Receipt proof: comparar request.TotalCents con order.TotalCents
   - Si no coincide → 400 "The provided order data does not match our records."
   - Mensaje generico para evitar oracle attacks
5. Validar Business.InvoicingEnabled == true
   - Si no → 400 "Electronic invoicing is not enabled for this business."
6. Validar orden no cancelada, no ya facturada
7. Buscar FiscalCustomer por (businessId, rfc)
   - Si existe → usar el existente, actualizar email si vino nuevo
   - Si no → crear FiscalCustomer nuevo
8. Llamar a la logica interna de RequestIndividualInvoiceAsync
   (reutilizar o refactorizar para aceptar FiscalCustomer directo)
9. Retornar IndividualInvoiceResult
```

---

## 6. Refactorizacion de InvoicingService

### 6.1 Problema actual

`RequestIndividualInvoiceAsync(string orderId, int fiscalCustomerId, int branchId)` hace:
- Validar orden existe y pertenece al `branchId`
- Validar `FiscalCustomer` existe por `fiscalCustomerId`
- Crear factura

El endpoint publico no tiene `branchId` ni `fiscalCustomerId`. Necesita enviar datos fiscales en crudo.

### 6.2 Solucion: Nuevo metodo en IInvoicingService

Agregar un metodo dedicado al flujo publico:

```
Task<IndividualInvoiceResult> RequestPublicInvoiceAsync(
    string orderId,
    int expectedTotalCents,
    PublicInvoiceCustomerData customerData)
```

**`PublicInvoiceCustomerData`** — DTO nuevo (en `IInvoicingService.cs`):

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `Rfc` | `string` | RFC del solicitante |
| `FiscalName` | `string` | Razon social |
| `TaxRegime` | `string` | Clave regimen fiscal SAT |
| `ZipCode` | `string` | CP fiscal |
| `Email` | `string?` | Email para envio |
| `CfdiUse` | `string?` | Uso CFDI (default G03) |

**Logica interna del metodo:**
1. Cargar Order con Branch → Business (eager load)
2. Receipt proof: `order.TotalCents != expectedTotalCents` → `ValidationException`
3. Validar `business.InvoicingEnabled`
4. Validar orden no cancelada, no facturada
5. Buscar o crear `FiscalCustomer` por `(businessId, rfc)`
6. Reusar la logica de mock Facturapi (actualizar `InvoiceStatus`, etc.)
7. Retornar `IndividualInvoiceResult`

### 6.3 Metodo de consulta publica

```
Task<PublicOrderInvoiceInfo> GetPublicOrderInfoAsync(string orderId)
```

**`PublicOrderInvoiceInfo`** — DTO nuevo:

| Campo | Tipo | Descripcion |
|-------|------|-------------|
| `OrderId` | `string` | UUID |
| `BranchName` | `string` | Nombre de la sucursal |
| `Date` | `DateTime` | Fecha de la orden |
| `TotalCents` | `int` | Total (tambien sirve como hint al cliente para el receipt proof) |
| `InvoiceStatus` | `string` | None, Pending, Issued, Cancelled |
| `InvoicingEnabled` | `bool` | Si el negocio tiene facturacion habilitada |
| `CanInvoice` | `bool` | Calculado: enabled + None + no cancelada + dentro de ventana |
| `InvoiceUrl` | `string?` | URL de descarga si ya esta facturada |

---

## 7. Rate Limiting

### 7.1 Nueva policy: `PublicInvoicingPolicy`

| Parametro | Valor |
|-----------|-------|
| Tipo | Fixed Window |
| PermitLimit | 10 |
| Window | 1 minuto |
| QueueLimit | 0 |
| Particion | Por IP remota |

### 7.2 Donde registrar

En `Program.cs`, dentro del bloque `AddRateLimiter`, agregar:

```
options.AddFixedWindowLimiter("PublicInvoicingPolicy", limiter =>
{
    limiter.PermitLimit = 10;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
});
```

Aplicar en el controller con `[EnableRateLimiting("PublicInvoicingPolicy")]`.

---

## 8. Seguridad — Threat Model

| Amenaza | Mitigacion |
|---------|-----------|
| Brute-force de UUIDs para descubrir ordenes | Rate limiting 10/min + UUIDs son 128-bit (inviable por fuerza bruta) |
| Facturacion no autorizada sin ticket | Receipt proof: `TotalCents` debe coincidir exactamente |
| Oracle attack para determinar montos | Mensaje generico "data does not match" sin indicar que campo fallo |
| Enumeracion de datos de negocio | DTO publico solo expone `BranchName`, `Date`, `TotalCents` — nada sensible |
| Spam de FiscalCustomers | Rate limiting + RFC se valida por formato antes de crear |
| Facturacion fuera de plazo SAT | Ventana temporal: max 40 dias desde `CreatedAt` |

---

## 9. Cambios Requeridos por Capa

### 9.1 POS.Services

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `IService/IInvoicingService.cs` | **MODIFICAR** | +2 metodos: `GetPublicOrderInfoAsync`, `RequestPublicInvoiceAsync` + 2 DTOs: `PublicOrderInvoiceInfo`, `PublicInvoiceCustomerData` |
| `Service/InvoicingService.cs` | **MODIFICAR** | Implementar los 2 metodos nuevos con receipt proof, FiscalCustomer upsert, y validacion de ventana temporal |

### 9.2 POS.API

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `Controllers/PublicInvoicingController.cs` | **CREAR** | `GET /api/invoicing/public/{orderId}` + `POST /api/invoicing/public/request` + request DTO |
| `Program.cs` | **MODIFICAR** | +`PublicInvoicingPolicy` rate limiter |

### 9.3 POS.Repository

**No se requieren cambios.** `IOrderRepository.GetAsync` con includes (`"Branch,Branch.Business"`) cubre la carga necesaria. `IFiscalCustomerRepository.GetByRfcAsync` ya existe.

### 9.4 POS.Domain

**No se requieren cambios.** Todos los modelos ya tienen los campos necesarios.

---

## 10. Resumen de Archivos

| Archivo | Accion | LOC estimado |
|---------|--------|-------------|
| `POS.Services/IService/IInvoicingService.cs` | **MODIFICAR** | +25 (2 firmas + 2 DTOs) |
| `POS.Services/Service/InvoicingService.cs` | **MODIFICAR** | +60 (2 implementaciones) |
| `POS.API/Controllers/PublicInvoicingController.cs` | **CREAR** | ~70 (controller + request DTO) |
| `POS.API/Program.cs` | **MODIFICAR** | +5 (rate limiter policy) |

**Total:** 1 archivo nuevo, 3 archivos modificados. ~160 lineas netas.

---

## 11. Lo que NO esta en scope

- Validacion de formato de RFC (regex + digito verificador) — se implementara en un servicio `IFiscalValidationService` separado.
- CAPTCHA o reCAPTCHA en el portal publico — decision de frontend.
- Envio de email con la factura al completarse — requiere webhook de Facturapi (Phase 15f).
- Cancelacion de factura desde el portal publico — solo el Owner puede cancelar.
- Localizacion de mensajes de error (actualmente todo en ingles en la API, el frontend traduce).
