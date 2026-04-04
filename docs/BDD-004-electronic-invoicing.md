# BDD-004 — Electronic Invoicing Integration (Facturapi)

**Fecha:** 2026-04-03
**Fase:** 15 — CFDI 4.0 Electronic Invoicing
**Autor:** Arquitecto Senior .NET
**Estado:** Pendiente de aprobacion

---

## 1. Resumen Ejecutivo

El sistema POS actualmente no tiene ninguna capacidad de facturacion electronica. No existen campos de RFC, regimen fiscal, claves SAT, ni tasas de impuestos en ningun modelo del codebase.

Este documento disena la integracion con **Facturapi** como proveedor de facturacion CFDI 4.0, soportando dos flujos criticos para negocios mexicanos:
- **Factura Global:** Consolidacion periodica de ventas al publico general (RFC generico `XAXX010101000`).
- **Factura Individual:** Emision bajo demanda para clientes que solicitan factura con su RFC.

---

## 2. Hallazgos Clave del Analisis

### 2.1 Estado actual — Zero infraestructura fiscal

| Componente | Tiene campos fiscales? | Detalle |
|------------|----------------------|---------|
| `Business.cs` | NO | Solo `Name`, `BusinessType`, `PlanType`. Sin RFC, regimen fiscal, ni direccion fiscal. |
| `Branch.cs` | NO | Solo `Name`, `LocationName`. Sin codigo postal fiscal, ni direccion. |
| `Product.cs` | NO | Solo `Name`, `PriceCents`. Sin clave SAT de producto/servicio, ni tasa de IVA. |
| `OrderItem.cs` | NO | Solo `ProductName`, `UnitPriceCents`, `Quantity`. Sin desglose de impuestos. |
| `Order.cs` | NO | Solo `TotalCents`, `SubtotalCents`. Sin `InvoiceId`, `InvoiceStatus`, ni campos fiscales. |
| `OrderPayment.cs` | NO | Sin `FormaDePago` SAT (01=Efectivo, 04=Tarjeta, 28=Transferencia). |

### 2.2 Facturapi — Modelo de integracion

Facturapi es un servicio REST que abstrae la complejidad del PAC (Proveedor Autorizado de Certificacion). El flujo es:

```
POS API → Facturapi REST API → PAC → SAT
                ↓
         Webhook de status
```

**Conceptos clave de Facturapi:**
- **Organization:** Representa al negocio emisor (RFC, regimen fiscal, CSD).
- **Customer:** Representa al receptor de la factura (RFC, razon social, regimen, CP, uso CFDI).
- **Invoice:** El CFDI emitido. Tiene status: `pending`, `valid`, `canceled`.
- **Global Invoice:** Factura global que consolida multiples ventas al publico general.

### 2.3 Requerimientos SAT para CFDI 4.0

| Campo | Donde vive | Requerido |
|-------|-----------|-----------|
| RFC Emisor | Business | SI — 12 o 13 chars |
| Regimen Fiscal Emisor | Business | SI — catalogo SAT (e.g., `601`, `612`, `621`) |
| Nombre Fiscal Emisor | Business | SI — razon social exacta ante SAT |
| CP Fiscal Emisor | Business | SI — 5 digitos |
| Clave Producto/Servicio | Product | SI — catalogo SAT (e.g., `90101500` = Alimentos) |
| Clave Unidad | Product | SI — catalogo SAT (e.g., `H87` = Pieza, `E48` = Servicio) |
| Tasa IVA | Product | SI — `0.16` (16%), `0.08` (frontera), `0` (exento) |
| RFC Receptor | Customer/Order | SI — `XAXX010101000` para publico general |
| Forma de Pago | OrderPayment | SI — catalogo SAT (e.g., `01`, `04`, `28`) |
| Metodo de Pago | Order | SI — `PUE` (Pago en Una sola Exhibicion) para POS |
| Uso CFDI | Customer | SI — `S01` (Sin obligaciones fiscales) para pub. gral., `G03` (Gastos generales) tipico |

### 2.4 Patron webhook existente — Reutilizable

El codebase ya tiene un patron probado con Stripe:
- `StripeWebhookController` → recibe, valida, inserta en inbox
- `StripeEventInbox` → tabla con idempotencia
- `StripeEventProcessorWorker` → background worker que procesa

Este patron es directamente reutilizable para webhooks de Facturapi.

---

## 3. Decisiones Arquitecturales

### 3.1 Donde viven los datos fiscales del emisor?

**Decision: En `Business`** (no en `Branch`).

**Justificacion:** En Mexico, el RFC y CSD pertenecen a la persona fisica o moral (el negocio), no a la sucursal. Todas las sucursales de un negocio emiten bajo el mismo RFC. El codigo postal de expedicion puede variar por sucursal, pero el regimen fiscal no.

Sin embargo, el **codigo postal de expedicion** (lugar desde donde se emite) SI vive en `Branch` — ya que cada sucursal puede estar en un CP diferente.

### 3.2 Donde vive la tasa de IVA?

**Decision: En `Product`.**

**Justificacion:** Cada producto puede tener tasa diferente:
- Alimentos preparados: 16% IVA (o 8% en zona fronteriza)
- Productos basicos (canasta basica): 0% IVA
- Servicios medicos: Exento

La tasa se configura a nivel de producto y se propaga al `OrderItem` al momento del sync para inmutabilidad.

### 3.3 Facturapi Organization ID — donde almacenarlo?

**Decision: En `Business`** como `FacturapiOrganizationId`.

Facturapi permite multiples "organizaciones" bajo una misma cuenta. Cada `Business` en nuestro sistema mapea a una Organization en Facturapi.

### 3.4 Factura Global vs. Individual — mismo modelo?

**Decision: SI.** Un unico modelo `Invoice` con un campo `InvoiceType` (`"global"` | `"individual"`). Las facturas globales simplemente usan el RFC generico `XAXX010101000` y agrupan multiples ordenes.

### 3.5 Facturapi Customer — donde?

**Decision: Nueva tabla `FiscalCustomer`** separada de `User`.

Un cliente fiscal NO es un usuario del sistema. Es un RFC con datos fiscales que el cajero captura cuando el cliente pide factura. Un mismo RFC puede pedir factura en multiples sucursales.

**Scope: `FiscalCustomer` pertenece a `Business`** (no a Branch), ya que un RFC es unico por negocio emisor.

---

## 4. Modelos de Dominio — Cambios y Nuevos

### 4.1 Business — Campos fiscales del emisor

| Propiedad | Tipo | MaxLength | Requerido | Descripcion |
|-----------|------|-----------|-----------|-------------|
| `Rfc` | `string?` | 13 | Condicional | RFC del negocio. 12 chars (moral) o 13 chars (fisica). Null hasta que configure facturacion. |
| `FiscalName` | `string?` | 300 | Condicional | Razon social exacta ante SAT. |
| `FiscalRegime` | `string?` | 3 | Condicional | Clave regimen fiscal SAT (e.g., `601` = General de Ley, `612` = Personas Fisicas). |
| `FacturapiOrganizationId` | `string?` | 50 | Condicional | ID de la organizacion en Facturapi. Null si no ha configurado facturacion. |
| `FacturapiApiKey` | `string?` | — (text) | Condicional | API Key de Facturapi para esta organizacion. Encriptada via `IDataProtector`. |
| `InvoicingEnabled` | `bool` | — | NO (default `false`) | Flag master: habilita/deshabilita facturacion electronica. |

### 4.2 Branch — Codigo postal de expedicion

| Propiedad | Tipo | MaxLength | Descripcion |
|-----------|------|-----------|-------------|
| `FiscalPostalCode` | `string?` | 5 | Codigo postal desde donde se expide la factura (lugar de expedicion). |

### 4.3 Product — Campos SAT

| Propiedad | Tipo | MaxLength | Default | Descripcion |
|-----------|------|-----------|---------|-------------|
| `SatProductCode` | `string?` | 10 | `null` | Clave de producto/servicio SAT (e.g., `90101500`). |
| `SatUnitCode` | `string?` | 5 | `null` | Clave de unidad SAT (e.g., `H87` = Pieza). |
| `TaxRatePercent` | `decimal?` | — | `null` | Tasa de IVA: `16.00`, `8.00`, `0.00`. Null = hereda default del negocio (16%). |

### 4.4 OrderItem — Desglose fiscal inmutable

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `TaxRatePercent` | `decimal?` | Tasa IVA congelada al momento de la orden. Copiada del Product. |
| `TaxAmountCents` | `int` | Monto de IVA en centavos. Calculado: `floor(UnitPriceCents * Quantity * TaxRate / (1 + TaxRate))`. |
| `SatProductCode` | `string?` (10) | Clave SAT congelada. Copiada del Product. |
| `SatUnitCode` | `string?` (5) | Clave unidad SAT congelada. Copiada del Product. |

**Justificacion de congelar:** El CFDI debe reflejar los datos fiscales **al momento de la venta**, no los datos actuales del producto (que podrian cambiar despues).

### 4.5 Order — Campos de facturacion

| Propiedad | Tipo | MaxLength | Default | Descripcion |
|-----------|------|-----------|---------|-------------|
| `InvoiceStatus` | `string?` | 20 | `null` | `null` = no facturada, `"pending"`, `"issued"`, `"cancelled"`. |
| `FacturapiInvoiceId` | `string?` | 50 | `null` | ID de la factura en Facturapi. |
| `InvoiceUrl` | `string?` | 500 | `null` | URL para descargar el PDF/XML de la factura. |
| `InvoicedAt` | `DateTime?` | — | `null` | Timestamp de emision del CFDI. |
| `FiscalCustomerId` | `int?` | — | `null` | FK a FiscalCustomer. Null para ordenes sin factura o con factura global. |

### 4.6 FiscalCustomer — Nuevo modelo

| Propiedad | Tipo | MaxLength | Descripcion |
|-----------|------|-----------|-------------|
| `Id` | `int` | — | PK |
| `BusinessId` | `int` | — | FK a Business. Un RFC es unico por negocio emisor. |
| `Rfc` | `string` | 13 | RFC del receptor. |
| `FiscalName` | `string` | 300 | Razon social del receptor. |
| `FiscalRegime` | `string` | 3 | Regimen fiscal SAT del receptor. |
| `PostalCode` | `string` | 5 | CP del domicilio fiscal del receptor. |
| `Email` | `string?` | 255 | Email para envio de factura. |
| `CfdiUse` | `string` | 5 | Uso CFDI default (e.g., `G03` = Gastos generales). |
| `FacturapiCustomerId` | `string?` | 50 | ID del cliente en Facturapi. |
| `CreatedAt` | `DateTime` | — | Creacion. |
| `UpdatedAt` | `DateTime?` | — | Ultima actualizacion. |

**Unique constraint:** `(BusinessId, Rfc)`.

### 4.7 Invoice — Nuevo modelo

| Propiedad | Tipo | MaxLength | Descripcion |
|-----------|------|-----------|-------------|
| `Id` | `int` | — | PK |
| `BusinessId` | `int` | — | FK a Business (emisor). |
| `BranchId` | `int` | — | FK a Branch (lugar de expedicion). |
| `FiscalCustomerId` | `int?` | — | FK a FiscalCustomer. Null para factura global (publico general). |
| `InvoiceType` | `string` | 20 | `"individual"` o `"global"`. |
| `FacturapiInvoiceId` | `string?` | 50 | ID en Facturapi. |
| `Series` | `string?` | 10 | Serie del CFDI (e.g., `"A"`). |
| `FolioNumber` | `string?` | 20 | Folio del CFDI. |
| `Status` | `string` | 20 | `"pending"`, `"valid"`, `"canceled"`. |
| `TotalCents` | `int` | — | Total de la factura en centavos. |
| `SubtotalCents` | `int` | — | Subtotal antes de impuestos. |
| `TaxCents` | `int` | — | Total de impuestos. |
| `Currency` | `string` | 3 | `"MXN"`. |
| `PaymentForm` | `string` | 2 | Forma de pago SAT (e.g., `01`, `04`). |
| `PaymentMethod` | `string` | 3 | Metodo de pago SAT: `"PUE"`. |
| `PdfUrl` | `string?` | 500 | URL del PDF. |
| `XmlUrl` | `string?` | 500 | URL del XML. |
| `CancellationReason` | `string?` | 2 | Clave SAT de cancelacion (e.g., `"02"` = Comprobante emitido con errores). |
| `IssuedAt` | `DateTime?` | — | Fecha de emision (timbrado). |
| `CanceledAt` | `DateTime?` | — | Fecha de cancelacion. |
| `CreatedAt` | `DateTime` | — | Creacion. |

### 4.8 InvoiceOrder — Tabla pivote (N:N)

Una factura global agrupa multiples ordenes. Una factura individual puede cubrir una orden. Una orden podria (edge case) tener multiples facturas si se cancela y re-emite.

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `InvoiceId` | `int` | FK a Invoice. |
| `OrderId` | `string` (36) | FK a Order. |

**PK compuesto:** `(InvoiceId, OrderId)`.

### 4.9 InvoiceStatus — Helper (string constants, no enum)

**Archivo:** `POS.Domain/Helpers/InvoiceStatus.cs`

| Constante | Valor | Descripcion |
|-----------|-------|-------------|
| `Pending` | `"pending"` | CFDI creado en Facturapi, esperando timbrado. |
| `Valid` | `"valid"` | CFDI timbrado y vigente ante SAT. |
| `Canceled` | `"canceled"` | CFDI cancelado ante SAT. |

### 4.10 SatPaymentForm — Helper (string constants)

**Archivo:** `POS.Domain/Helpers/SatPaymentForm.cs`

Mapeo de `PaymentMethod` enum a clave SAT:

| PaymentMethod | Clave SAT | Descripcion SAT |
|---------------|-----------|-----------------|
| `Cash` | `01` | Efectivo |
| `Card` | `04` | Tarjeta de credito |
| `Transfer` | `03` | Transferencia electronica |
| `Clip` | `04` | Tarjeta de credito (terminal) |
| `MercadoPago` | `04` | Tarjeta de credito (QR) |
| `BankTerminal` | `04` | Tarjeta de credito (terminal bancario) |
| `Other` | `99` | Por definir |

---

## 5. Servicios

### 5.1 IInvoicingService

| Metodo | Descripcion |
|--------|-------------|
| `CreateIndividualInvoiceAsync(string orderId, int fiscalCustomerId, int branchId)` | Emite CFDI individual para una orden especifica. Requiere datos fiscales del cliente. |
| `CreateGlobalInvoiceAsync(int branchId, DateTime periodStart, DateTime periodEnd)` | Emite factura global consolidando ordenes no facturadas del periodo. |
| `CancelInvoiceAsync(int invoiceId, string cancellationReason)` | Cancela un CFDI ante SAT via Facturapi. |
| `GetInvoiceStatusAsync(int invoiceId)` | Consulta estado actual en Facturapi. |
| `DownloadInvoiceAsync(int invoiceId, string format)` | Retorna URL de descarga (PDF o XML). |
| `ProcessWebhookEventAsync(FacturapiWebhookInbox event)` | Procesa webhook de cambio de status. |

### 5.2 IFiscalCustomerService

| Metodo | Descripcion |
|--------|-------------|
| `CreateOrUpdateAsync(int businessId, FiscalCustomer customer)` | Crea o actualiza cliente fiscal. Sincroniza con Facturapi. |
| `GetByRfcAsync(int businessId, string rfc)` | Busca cliente por RFC dentro del negocio. |
| `GetByBusinessAsync(int businessId)` | Lista todos los clientes fiscales del negocio. |
| `DeleteAsync(int id)` | Elimina cliente fiscal (soft delete o hard si no tiene facturas). |

### 5.3 IFiscalConfigService

| Metodo | Descripcion |
|--------|-------------|
| `SetupEmitterAsync(int businessId, FiscalSetupRequest request)` | Configura RFC, regimen, razon social del emisor. Crea Organization en Facturapi. |
| `GetEmitterConfigAsync(int businessId)` | Retorna configuracion fiscal del emisor. |
| `ValidateRfcAsync(string rfc)` | Valida formato de RFC (regex + digito verificador). |

---

## 6. API Endpoints

### 6.1 Configuracion Fiscal del Emisor

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `PUT /api/invoicing/config` | PUT | Owner | Configura datos fiscales del emisor (RFC, regimen, razon social, CP) |
| `GET /api/invoicing/config` | GET | Owner, Manager | Obtiene configuracion fiscal del emisor |

### 6.2 Clientes Fiscales

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `GET /api/invoicing/customers` | GET | Owner, Manager, Cashier | Lista clientes fiscales del negocio |
| `GET /api/invoicing/customers/{rfc}` | GET | Owner, Manager, Cashier | Busca cliente por RFC |
| `POST /api/invoicing/customers` | POST | Owner, Manager, Cashier | Crea o actualiza cliente fiscal |
| `DELETE /api/invoicing/customers/{id}` | DELETE | Owner | Elimina cliente fiscal |

### 6.3 Emision de Facturas

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `POST /api/invoicing/individual` | POST | Owner, Manager, Cashier | Emite factura individual para una orden |
| `POST /api/invoicing/global` | POST | Owner, Manager | Emite factura global para un periodo |
| `POST /api/invoicing/{id}/cancel` | POST | Owner | Cancela factura ante SAT |
| `GET /api/invoicing/{id}` | GET | Owner, Manager, Cashier | Obtiene detalle de factura |
| `GET /api/invoicing/{id}/download/{format}` | GET | Owner, Manager, Cashier | Descarga PDF o XML |
| `GET /api/invoicing/by-order/{orderId}` | GET | Owner, Manager, Cashier | Obtiene facturas de una orden |

### 6.4 Webhook de Facturapi

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `POST /api/facturapi/webhook` | POST | `[AllowAnonymous]` | Recibe notificaciones de Facturapi |

---

## 7. Flujo: Factura Individual

```
1. Cliente pide factura al cajero
2. Cajero busca RFC en el POS → GET /api/invoicing/customers/{rfc}
3. Si no existe: cajero captura datos → POST /api/invoicing/customers
4. Cajero solicita factura → POST /api/invoicing/individual
   Body: { orderId, fiscalCustomerId }
5. Backend:
   a. Valida que la orden existe, pertenece al branch, no esta cancelada
   b. Valida que el business tiene InvoicingEnabled + RFC configurado
   c. Obtiene el FiscalCustomer → obtiene o crea Customer en Facturapi
   d. Construye los Items del CFDI desde OrderItems (con claves SAT + IVA)
   e. Determina FormaDePago desde OrderPayments → SatPaymentForm mapping
   f. Llama a Facturapi API → crea Invoice
   g. Guarda Invoice + InvoiceOrder en DB
   h. Actualiza Order.InvoiceStatus = "pending", Order.FacturapiInvoiceId = id
6. Facturapi procesa → webhook → status = "valid"
7. Backend actualiza Invoice.Status = "valid", Order.InvoiceStatus = "issued"
8. Frontend muestra boton "Descargar factura"
```

---

## 8. Flujo: Factura Global

```
1. Manager/Owner solicita factura global → POST /api/invoicing/global
   Body: { periodStart, periodEnd }
2. Backend:
   a. Busca ordenes del branch en el periodo que NO tienen factura
      (InvoiceStatus == null AND CancellationReason == null AND IsPaid == true)
   b. Agrupa items y calcula totales + IVA
   c. Usa RFC generico XAXX010101000, Regimen 616, Uso S01
   d. Llama a Facturapi API → crea Global Invoice
   e. Guarda Invoice + InvoiceOrder (multiples ordenes) en DB
   f. Actualiza cada Order.InvoiceStatus = "pending"
3. Facturapi webhook → status = "valid"
4. Backend actualiza todas las ordenes vinculadas
```

---

## 9. Flujo: Webhook de Facturapi

**Patron:** Identico a Stripe — Transactional Inbox.

### 9.1 FacturapiWebhookInbox — Nuevo modelo

| Propiedad | Tipo | MaxLength | Descripcion |
|-----------|------|-----------|-------------|
| `Id` | `int` | — | PK |
| `FacturapiEventId` | `string` | 255 | ID del evento. Unique constraint para idempotencia. |
| `EventType` | `string` | 100 | Tipo (e.g., `"invoice.status_updated"`). |
| `RawJson` | `string` | text | Payload completo. |
| `Status` | `string` | 20 | `"pending"`, `"processed"`, `"failed"`. |
| `CreatedAt` | `DateTime` | — | Recepcion. |
| `ProcessedAt` | `DateTime?` | — | Procesamiento. |
| `ErrorMessage` | `string?` | 2000 | Detalle de error. |

### 9.2 Background Worker: `FacturapiWebhookProcessorWorker`

- Poll cada 10 segundos (las facturas no son tan urgentes como pagos)
- Batch de 20 eventos
- Eventos manejados:
  - `invoice.status_updated` → Actualizar `Invoice.Status` + `Order.InvoiceStatus`
  - `invoice.canceled` → Marcar como cancelada

---

## 10. Impacto en el Sync Engine

### 10.1 SyncOrderRequest / SyncOrderItemRequest — Sin cambios

Los campos fiscales (`TaxRatePercent`, `SatProductCode`, `SatUnitCode`) se **resuelven en el backend** durante el mapping, no los envia el frontend. El frontend sigue enviando el request actual sin cambios.

### 10.2 MapToOrderItem — Enriquecimiento fiscal

En `OrderService.MapToOrderItem`, despues de mapear los campos actuales, el backend **podria** enriquecer con datos fiscales del producto. Sin embargo, esto requiere cargar los productos, lo cual impactaria performance.

**Decision: Enriquecimiento lazy.** Los campos fiscales de `OrderItem` se rellenan **al momento de emitir la factura**, no durante el sync. Esto evita impacto en performance del sync y permite que ordenes se sincronicen sin configuracion fiscal.

### 10.3 CashRegisterSessionId — Trazabilidad preservada

La cadena de trazabilidad se extiende:
```
CashRegisterSession → Order → Invoice (via InvoiceOrder) → CFDI
```

Esto permite reportes como: "En el turno de caja #47, se emitieron 3 facturas individuales por $12,450 MXN y 1 factura global por $45,200 MXN".

---

## 11. Calculo de Impuestos

### 11.1 Precios en el POS — IVA incluido

En Mexico, los precios al publico **siempre incluyen IVA**. El `PriceCents` del producto ya tiene IVA incluido. Para la factura, se desglosa:

```
Subtotal = PriceCents / (1 + TaxRate)
IVA      = PriceCents - Subtotal
```

**Ejemplo:** Taco a $50.00 MXN (IVA 16%):
```
Subtotal = 5000 / 1.16 = 4310.34 → 4310 centavos
IVA      = 5000 - 4310 = 690 centavos
```

### 11.2 Precision

Facturapi maneja decimales. El calculo se hace en el `InvoicingService` al momento de emitir, no durante el sync. Se usa `decimal` para precision y se redondea al final segun reglas SAT.

### 11.3 Productos sin tasa configurada

Si `Product.TaxRatePercent == null`, se usa el default de **16%** (tasa general). Esto se documenta como constante en el servicio.

---

## 12. Configuracion y Settings

### 12.1 FacturapiSettings

**Archivo:** `POS.Domain/Settings/FacturapiSettings.cs`

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `ApiKey` | `string` | API Key master de Facturapi (cuenta de la plataforma POS). |
| `WebhookSecret` | `string` | Secret para validar webhooks de Facturapi. |
| `IsSandbox` | `bool` | `true` para ambiente de pruebas. |

**Nota:** `FacturapiApiKey` a nivel de `Business` es la API Key especifica de la Organization. `FacturapiSettings.ApiKey` es la key master de la plataforma.

### 12.2 appsettings.json

```json
{
  "Facturapi": {
    "ApiKey": "",
    "WebhookSecret": "",
    "IsSandbox": true
  }
}
```

---

## 13. Cambios Requeridos por Capa

### 13.1 POS.Domain

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `Models/Business.cs` | **MODIFICAR** | +6 propiedades fiscales del emisor |
| `Models/Branch.cs` | **MODIFICAR** | +1 propiedad `FiscalPostalCode` |
| `Models/Product.cs` | **MODIFICAR** | +3 propiedades SAT (`SatProductCode`, `SatUnitCode`, `TaxRatePercent`) |
| `Models/OrderItem.cs` | **MODIFICAR** | +4 propiedades fiscales inmutables |
| `Models/Order.cs` | **MODIFICAR** | +5 propiedades de facturacion |
| `Models/FiscalCustomer.cs` | **CREAR** | Cliente fiscal con datos SAT |
| `Models/Invoice.cs` | **CREAR** | Factura CFDI con status y URLs |
| `Models/InvoiceOrder.cs` | **CREAR** | Tabla pivote Invoice ↔ Order |
| `Models/FacturapiWebhookInbox.cs` | **CREAR** | Inbox para webhooks de Facturapi |
| `Helpers/InvoiceStatus.cs` | **CREAR** | Constantes: `Pending`, `Valid`, `Canceled` |
| `Helpers/SatPaymentForm.cs` | **CREAR** | Mapeo PaymentMethod → clave SAT |
| `Settings/FacturapiSettings.cs` | **CREAR** | Configuracion de Facturapi |

### 13.2 POS.Repository

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `ApplicationDbContext.cs` | **MODIFICAR** | Config para nuevas entidades + campos en Business, Branch, Product, OrderItem, Order |
| `IRepository/IInvoiceRepository.cs` | **CREAR** | Queries de facturas por branch, orden, periodo |
| `Repository/InvoiceRepository.cs` | **CREAR** | Implementacion |
| `IRepository/IFiscalCustomerRepository.cs` | **CREAR** | Queries por RFC, Business |
| `Repository/FiscalCustomerRepository.cs` | **CREAR** | Implementacion |
| `IRepository/IFacturapiWebhookInboxRepository.cs` | **CREAR** | `GetPendingEventsAsync` |
| `Repository/FacturapiWebhookInboxRepository.cs` | **CREAR** | Implementacion |
| `IUnitOfWork.cs` | **MODIFICAR** | +3 repositorios |
| `UnitOfWork.cs` | **MODIFICAR** | Lazy init |
| `Migrations/` | **CREAR** | Migration masiva |

### 13.3 POS.Services

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `IService/IInvoicingService.cs` | **CREAR** | Interface con metodos de emision y cancelacion |
| `Service/InvoicingService.cs` | **CREAR** | Implementacion con llamadas a Facturapi API |
| `IService/IFiscalCustomerService.cs` | **CREAR** | Interface CRUD de clientes fiscales |
| `Service/FiscalCustomerService.cs` | **CREAR** | Implementacion |
| `IService/IFiscalConfigService.cs` | **CREAR** | Interface de configuracion fiscal |
| `Service/FiscalConfigService.cs` | **CREAR** | Implementacion |

### 13.4 POS.API

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `Controllers/InvoicingController.cs` | **CREAR** | Endpoints de configuracion, emision, descarga |
| `Controllers/FiscalCustomerController.cs` | **CREAR** | CRUD clientes fiscales |
| `Controllers/FacturapiWebhookController.cs` | **CREAR** | Webhook receiver |
| `Workers/FacturapiWebhookProcessorWorker.cs` | **CREAR** | Background processor |
| `Program.cs` | **MODIFICAR** | Registrar services, settings, worker |

---

## 14. Schema: Tablas y Migrations

### 14.1 Business — Columnas nuevas

| Columna | Tipo SQL | Nullable | Indice |
|---------|----------|----------|--------|
| `Rfc` | `varchar(13)` | YES | YES (unique per business) |
| `FiscalName` | `varchar(300)` | YES | NO |
| `FiscalRegime` | `varchar(3)` | YES | NO |
| `FacturapiOrganizationId` | `varchar(50)` | YES | NO |
| `FacturapiApiKey` | `text` | YES | NO |
| `InvoicingEnabled` | `boolean` | NO (default false) | NO |

### 14.2 Branch — Columna nueva

| Columna | Tipo SQL | Nullable |
|---------|----------|----------|
| `FiscalPostalCode` | `varchar(5)` | YES |

### 14.3 Product — Columnas nuevas

| Columna | Tipo SQL | Nullable |
|---------|----------|----------|
| `SatProductCode` | `varchar(10)` | YES |
| `SatUnitCode` | `varchar(5)` | YES |
| `TaxRatePercent` | `decimal(5,2)` | YES |

### 14.4 OrderItem — Columnas nuevas

| Columna | Tipo SQL | Nullable | Default |
|---------|----------|----------|---------|
| `TaxRatePercent` | `decimal(5,2)` | YES | NULL |
| `TaxAmountCents` | `integer` | NO | 0 |
| `SatProductCode` | `varchar(10)` | YES | NULL |
| `SatUnitCode` | `varchar(5)` | YES | NULL |

### 14.5 Order — Columnas nuevas

| Columna | Tipo SQL | Nullable | Indice |
|---------|----------|----------|--------|
| `InvoiceStatus` | `varchar(20)` | YES | YES (filtrado: WHERE NOT NULL) |
| `FacturapiInvoiceId` | `varchar(50)` | YES | YES |
| `InvoiceUrl` | `varchar(500)` | YES | NO |
| `InvoicedAt` | `timestamp` | YES | NO |
| `FiscalCustomerId` | `integer` | YES (FK) | YES |

### 14.6 FiscalCustomer — Tabla nueva

PK: `Id` serial. Unique: `(BusinessId, Rfc)`.

### 14.7 Invoice — Tabla nueva

PK: `Id` serial. Indices: `BranchId`, `FacturapiInvoiceId`, `Status`.

### 14.8 InvoiceOrder — Tabla nueva

PK compuesto: `(InvoiceId, OrderId)`. FKs con cascade.

### 14.9 FacturapiWebhookInbox — Tabla nueva

PK: `Id` serial. Unique: `FacturapiEventId`. Indice: `Status`.

---

## 15. Backward Compatibility

- Todas las columnas nuevas en tablas existentes son **nullable** o tienen **default values**.
- `Business.InvoicingEnabled` default `false` — facturacion deshabilitada hasta configurar.
- `Product.TaxRatePercent` null — se asume 16% al momento de facturar.
- `Order.InvoiceStatus` null — ordenes no facturadas (estado actual de todas las ordenes).
- **Zero breaking changes** para frontends que no usen facturacion.

---

## 16. Orden de Implementacion Sugerido

| Subfase | Descripcion | Dependencias |
|---------|-------------|--------------|
| **15a** | Campos fiscales en Business, Branch, Product, OrderItem, Order + FiscalCustomer + Invoice + InvoiceOrder + migration | Ninguna |
| **15b** | FacturapiSettings + FacturapiWebhookInbox + repos + UoW + migration | 15a |
| **15c** | IFiscalConfigService + IFiscalCustomerService + endpoints de configuracion | 15a |
| **15d** | IInvoicingService (factura individual) + InvoicingController + integracion con API de Facturapi | 15a, 15b, 15c |
| **15e** | Factura global (consolidacion de ordenes) | 15d |
| **15f** | FacturapiWebhookController + FacturapiWebhookProcessorWorker | 15b, 15d |

**Phase 15a es el foundation** — schema y modelos. Se puede implementar independientemente.

---

## 17. Lo que NO esta en scope

- Subir CSD (Certificado de Sello Digital) a Facturapi — se asume configurado via dashboard de Facturapi.
- Cancelacion con aceptacion del receptor (flujo asincronico del SAT).
- Complementos de pago (solo `PUE` por ahora, no `PPD`).
- Notas de credito (CFDI tipo Egreso).
- Consulta de estatus ante SAT (solo via Facturapi).
- Catalogo SAT de productos/servicios en DB local (el cajero ingresa la clave manualmente o se preconfigura en Product).
- Facturacion para ordenes de delivery (las plataformas facturan directamente al consumidor).
- Reportes fiscales (complemento de contabilidad electronica).
