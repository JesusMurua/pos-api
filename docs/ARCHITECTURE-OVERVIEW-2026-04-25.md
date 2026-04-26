# Reporte Arquitectónico — pos-api

**Fecha:** 2026-04-25 · **Branch:** main · **Stack:** .NET 10 / EF Core / PostgreSQL / JWT
**Alcance:** auditoría read-only completa del backend, sin modificaciones.
**Audiencia:** equipo de diseño/marketing para construcción de landing page y materiales de venta.

---

## 1. ENTIDADES DE NEGOCIO

### 1.1 Entidades principales (`POS.Domain/Models/`)

| Entidad | Propiedades clave | Relaciones | Enum asociado |
|---|---|---|---|
| **Business** | Name, PrimaryMacroCategoryId, CustomGiroDescription, PlanTypeId, TrialEndsAt, TrialUsed, OnboardingCompleted, OnboardingStatusId, CurrentOnboardingStep, CountryCode, Rfc, TaxRegime, LegalName, InvoicingEnabled, FacturapiOrganizationId, LoyaltyEnabled, PointsPerCurrencyUnit, CurrencyUnitsPerPoint, PointRedemptionValueCents | PrimaryMacroCategory, PlanTypeCatalog, OnboardingStatus, BusinessGiros, Branches, Users, Subscription | — |
| **Branch** | BusinessId, Name, LocationName, PinHash, IsMatrix, FolioPrefix, FolioCounter, FolioFormat, HasKitchen, HasTables, HasDelivery, FiscalZipCode, TimeZoneId | Business, Categories, Orders, UserBranches, Reservations, Suppliers, StockReceipts, DeliveryConfigs, CashRegisters, PaymentConfigs, Devices, Products, Zones, Promotions | — |
| **User** | BusinessId, BranchId, Name, Email, PasswordHash, PinHash, RoleId | RoleCatalog, Business, Branch, Orders, UserBranches | UserRole |
| **Order** | BranchId, UserId, OrderNumber, TotalCents, PaidCents, ChangeCents, SubtotalCents, TaxAmountCents, OrderDiscountCents, TotalDiscountCents, KitchenStatusId, FolioNumber, OrderSource, ExternalOrderId, DeliveryStatus, DeliveryCustomerName, EstimatedPickupAt, TableId, TableName, CashRegisterSessionId, IsOrphaned, CustomerId, InvoiceStatus, FacturapiId, InvoiceUrl, FiscalCustomerId, InvoiceId | KitchenStatusCatalog, SyncStatusCatalog, Branch, User, Table, CashRegisterSession, FiscalCustomer, Invoice, Customer, Items, Payments | OrderSource, DeliveryStatus, KitchenStatus, OrderSyncStatus, InvoiceStatus |
| **OrderItem** | OrderId, ProductId, ProductName, Quantity, UnitPriceCents, SizeName, ExtrasJson, Notes, DiscountCents, PromotionId, SatProductCode, SatUnitCode, TaxRatePercent, TaxAmountCents, **Metadata** (JSON) | Order, Product, AppliedTaxes | — |
| **OrderPayment** | OrderId, Method, AmountCents, Reference, PaymentProvider, ExternalTransactionId, PaymentMetadata, OperationId, PaymentStatusId, ConfirmedAt | Order, PaymentStatus | PaymentMethod |
| **Product** | CategoryId, BranchId, Name, PriceCents, ImageUrl, Description, Barcode, IsAvailable, IsPopular, TrackStock, CurrentStock, LowStockThreshold, SatProductCode, SatUnitCode, TaxRate, IsTaxIncluded, PrintingDestination, **Metadata** (JSON) | Category, Branch, Sizes, ModifierGroups, Images, ProductTaxes, ProductConsumptions | PrintingDestination |
| **Category** | BranchId, Name, Icon, SortOrder, IsActive | Branch, Products | — |
| **ProductSize** | ProductId, Label, ExtraPriceCents | Product | — |
| **ProductModifierGroup** | ProductId, Name, SortOrder, IsRequired, MinSelectable, MaxSelectable | Product, Extras | — |
| **ProductExtra** | ProductModifierGroupId, Label, PriceCents, SortOrder | ProductModifierGroup | — |
| **ProductImage** | ProductId, Url, SortOrder | Product | — |
| **ProductTax** | ProductId, TaxId | Product, Tax | — |
| **ProductConsumption** | ProductId, InventoryItemId, QuantityPerSale | Product, InventoryItem | — |
| **InventoryItem** | BranchId, Name, Unit, UnitOfMeasure, CurrentStock, LowStockThreshold, CostCents | Branch, Movements, ProductConsumptions | UnitOfMeasure |
| **InventoryMovement** | InventoryItemId, ProductId, TransactionType, InventoryMovementTypeId, Quantity, StockAfterTransaction, Reason, OrderId | InventoryItem, InventoryMovementType | InventoryTransactionType |
| **Supplier** | BranchId, Name, ContactName, Phone, Notes | Branch, StockReceipts | — |
| **StockReceipt** | BranchId, SupplierId, ReceivedByUserId, ReceivedAt, Notes, TotalCents | Branch, Supplier, ReceivedBy, Items | — |
| **StockReceiptItem** | StockReceiptId, InventoryItemId, ProductId, Quantity, CostCents, TotalCents | StockReceipt, InventoryItem, Product | — |
| **Customer** | BusinessId, FirstName, LastName, Phone, Email, PointsBalance, CreditBalanceCents, CreditLimitCents, **MembershipValidUntil**, **LastPaymentAt** | Business, Orders, Reservations, Transactions | — |
| **CustomerTransaction** | CustomerId, BranchId, TransactionType, AmountCents, PointsAmount, BalanceAfterCents, ReferenceOrderId | Customer, Branch, ReferenceOrder | CustomerTransactionType |
| **FiscalCustomer** | BusinessId, Rfc, BusinessName, TaxRegime, ZipCode, Email, CfdiUse, FacturapiCustomerId, CustomerId | Business, Customer | — |
| **Invoice** | BusinessId, BranchId, Type, Status, FacturapiId, FiscalCustomerId, Series, FolioNumber, TotalCents, SubtotalCents, TaxCents, PdfUrl, XmlUrl, IssuedAt, CancelledAt | Business, Branch, FiscalCustomer, Orders | InvoiceType, InvoiceStatus |
| **Tax** | CountryCode, Name, Rate, Code, IsDefault | ProductTaxes | — |
| **Promotion** | BranchId, Name, PromotionTypeId, AppliesTo, Value, MinQuantity, PaidQuantity, FreeProductId, CategoryId, ProductId, DaysOfWeek, StartsAt, EndsAt, MinOrderCents, MaxUsesTotal, CouponCode, IsStackable | PromotionTypeCatalog, Branch, Usages | PromotionScope |
| **RestaurantTable** | BranchId, Name, Capacity, TableStatusId, ZoneId | TableStatus, Branch, Zone, Orders, Reservations | — |
| **Reservation** | BranchId, TableId, GuestName, GuestPhone, PartySize, ReservationDate, ReservationTime, DurationMinutes, Status, CustomerId | Branch, Table, CreatedByUser, Customer | ReservationStatus |
| **Zone** | BranchId, Name, Type, SortOrder | Branch | ZoneType |
| **CashRegister** | BranchId, Name, DeviceUuid | Branch, Sessions | — |
| **CashRegisterSession** | BranchId, OpenedBy, OpenedAt, InitialAmountCents, ClosedBy, ClosedAt, CountedAmountCents, CashSalesCents, ExpectedAmountCents, DifferenceCents, CashRegisterStatusId, CashRegisterId | CashRegisterStatusCatalog, Branch, CashRegister, Movements, Orders | — |
| **Device** | BranchId, DeviceUuid, Mode, Name, IsActive, LastSeenAt | Branch | — |
| **DeviceActivationCode** | BranchId, Code, Mode, Name, IsUsed, ExpiresAt, CreatedBy | Branch | — |
| **BusinessGiro** | BusinessId, BusinessTypeId | Business, BusinessTypeCatalog | — |
| **Subscription** | BusinessId, StripeCustomerId, StripeSubscriptionId, StripePriceId, PlanTypeId, BillingCycle, PricingGroup, Status, TrialEndsAt, CurrentPeriodStart, CurrentPeriodEnd, CanceledAt | Business, PlanTypeCatalog | — |
| **AuditLog** | UserId, EntityName, EntityId, Action, Changes (JSON) | User | — |
| **PrintJob** | BranchId, OrderId, Destination, Status, Content, Attempts | Order, Branch | PrintJobStatus |
| **PushSubscription** | UserId, Endpoint, P256dh, Auth | User | — |
| **StripeEventInbox** | StripeEventId, Type, RawJson, Status, ErrorMessage | — | — |
| **PaymentWebhookInbox** | Provider, ExternalEventId, RawPayload, Status | — | — |
| **KdsEventOutbox** | BranchId, EventType, Payload, Processed | Branch | — |

### 1.2 Enums (`POS.Domain/Enums/`)

| Enum | Valores |
|---|---|
| **PaymentMethod** | Cash, Card, Transfer, Other, Clip, MercadoPago, BankTerminal, StoreCredit, LoyaltyPoints |
| **OrderSource** | Direct (0), UberEats (1), Rappi (2), DidiFood (3) |
| **OrderSyncStatus** | Pending, Synced, Failed |
| **DeliveryStatus** | PendingAcceptance, Accepted, Ready, PickedUp, Rejected |
| **KitchenStatus** | Pending, Ready, Delivered |
| **InvoiceStatus** | None, Pending, Issued, Cancelled |
| **InvoiceType** | Individual, Global |
| **InventoryTransactionType** | Purchase, ConsumeFromSale, Waste, ManualAdjustment, InitialCount |
| **UnitOfMeasure** | Kg, G, L, mL, Pcs, Oz |
| **UserRole** | Owner, Manager, Cashier, Kitchen, Waiter, Kiosk, Host |
| **ReservationStatus** | Pending, Confirmed, Seated, Cancelled, NoShow |
| **PlanType** | Free, Basic, Pro, Enterprise |
| **CustomerTransactionType** | EarnPoints, RedeemPoints, AddCredit, UseCredit, CreditAdjustment, PointsAdjustment |
| **PrintingDestination** | Kitchen, Bar, Waiters |
| **PrintJobStatus** | Pending, Printed, Failed, InProgress |
| **PromotionType** | Percentage, Fixed, Bogo, Bundle, OrderDiscount, FreeProduct |
| **PromotionScope** | All, Category, Product |
| **PosExperience** | Restaurant, Counter, Retail, Quick |
| **ZoneType** | Salon, BarSeats, Other |

---

## 2. MACRO-CATEGORÍAS Y SUB-GIROS

### 2.1 MacroCategory (4 macros — drivers de UX y feature gating)

| Id | InternalCode | PublicName | PosExperience | HasKitchen | HasTables |
|---|---|---|---|---|---|
| 1 | food-beverage | Restaurantes y Bares | Restaurant | ✓ | ✓ |
| 2 | quick-service | Comida Rápida y Cafés | Counter | ✓ | ✗ |
| 3 | retail | Tiendas y Comercios | Retail | ✗ | ✗ |
| 4 | services | Servicios Especializados | Services | ✗ | ✗ |

### 2.2 BusinessTypeCatalog (20 sub-giros)

**Food & Beverage (Macro 1):** Restaurante (1), Bar/Cantina (2), Sports Bar/Wings (3)
**Quick Service (Macro 2):** Taquería (4), Dogos (5), Hamburguesas (6), Cafetería (7), Paletería/Nevería (8), Panadería/Repostería (9)
**Retail (Macro 3):** Abarrotes/Miscelánea (10), Expendio/Cerveza (11), Refaccionaria (12), Ferretería (13), Papelería (14), Farmacia (15), Boutique (16)
**Services (Macro 4):** Estética/Barbería (17), Taller Mecánico (18), Consultorio/Clínica (19), Gimnasio/Deportes (20)

### 2.3 Modelo de relaciones

```
MacroCategory ──1:N──▶ BusinessTypeCatalog (sub-giros)
       ▲                          ▲
       │ N:1                      │ N:M
       │                          │
    Business ──1:N──▶ BusinessGiro ──N:1──▶ BusinessTypeCatalog
       │
       └─ CustomGiroDescription (texto libre cuando elige "Otro")
```

Un `Business` ancla a una **MacroCategory primaria** (drives planes/features) y selecciona **N sub-giros** vía `BusinessGiro`. La macro es lógica interna; los sub-giros son la cara pública.

---

## 3. PLANES Y FEATURES

### 3.1 Planes

| Id | InternalCode | Nombre | Precio mensual | Precio anual | Trial |
|---|---|---|---|---|---|
| 1 | Free | Gratis | $0 | — | — |
| 2 | Basic | Básico | $149 MXN | Tarifa anual con descuento | 14 días |
| 3 | Pro | Pro | $349 MXN | Tarifa anual con descuento | 14 días |
| 4 | Enterprise | Enterprise | Contactar a ventas | Contactar a ventas | 14 días |

### 3.2 Pricing Groups (precios diferenciados por macro)

| PricingGroup | Aplica a |
|---|---|
| **Restaurant** | Food & Beverage (premium) |
| **Standard** | Quick Service |
| **General** | Retail, Services |

Un mismo plan tiene 3 SKUs en Stripe: e.g. *Pro Restaurant Annual* ≠ *Pro General Annual*. Total: 18 Stripe price IDs sembrados (4 planes × 3 grupos × 2 ciclos, descontando Free).

### 3.3 Catálogo completo de Features (`FeatureKey` enum)

| Id | Code | Categoría | Tipo |
|---|---|---|---|
| 1 | CoreHardware | Hardware | Boolean |
| 10 | MaxProducts | Límite | Numérico |
| 11 | MaxUsers | Límite | Numérico |
| 12 | MaxBranches | Límite | Numérico |
| 13 | MaxCashRegisters | Límite | Numérico |
| 20 | CfdiInvoicing | Fiscal | Boolean |
| 30 | KdsBasic | Cocina | Boolean |
| 31 | RealtimeKds | Cocina | Boolean |
| 32 | PrintedCommandaTickets | Cocina | Boolean |
| 40 | TableMap | Servicio | Boolean |
| 41 | WaiterApp | Servicio | Boolean |
| 42 | KioskMode | Servicio | Boolean |
| 43 | TableService | Servicio | Boolean |
| 50 | RecipeInventory | Inventario | Boolean |
| 51 | MultiWarehouseInventory | Inventario | Boolean |
| 52 | StockAlerts | Inventario | Boolean |
| 60 | StoreCredit | CRM | Boolean |
| 61 | ComparativeReports | Reportes | Boolean |
| 62 | AdvancedReports | Reportes | Boolean |
| 70 | LoyaltyCrm | CRM | Boolean |
| 71 | CustomerDatabase | CRM | Boolean |
| 80 | SimpleFolios | Fiscal | Boolean |
| 81 | CustomFolios | Fiscal | Boolean |
| 82 | AppointmentReminders | Servicios | Boolean |
| 90 | PublicApi | Integraciones | Boolean |
| 91 | MultiBranch | Multi-tenant | Boolean |
| 100 | ProviderPayments | Pagos | Boolean |
| 110 | DeliveryPlatforms | Integraciones | Boolean |

### 3.4 Matriz Plan × Feature

| Feature | Free | Basic | Pro | Enterprise |
|---|---|---|---|---|
| CoreHardware | ✓ | ✓ | ✓ | ✓ |
| MaxProducts | 50 (500 Retail) | ∞ | ∞ | ∞ |
| MaxUsers | 3 | ∞ | ∞ | ∞ |
| MaxBranches | 1 | 1 | 1 | ∞ |
| MaxCashRegisters | 1 | 1 | ∞ | ∞ |
| CfdiInvoicing | ✗ | ✓ | ✓ | ✓ |
| KdsBasic | ✗ | ✓ | ✓ | ✓ |
| RealtimeKds | ✗ | ✗* | ✓ | ✓ |
| PrintedCommandaTickets | ✓ | ✓ | ✓ | ✓ |
| TableMap | ✗ | ✗ | ✓ | ✓ |
| WaiterApp | ✗ | ✗ | ✓ | ✓ |
| KioskMode | ✗ | ✗ | ✓ | ✓ |
| TableService | ✗ | ✓ | ✓ | ✓ |
| RecipeInventory | ✗ | ✗ | ✗ | ✓ |
| MultiWarehouseInventory | ✗ | ✗ | ✓ | ✓ |
| StockAlerts | ✗ | ✗ | ✓ | ✓ |
| StoreCredit | ✗ | ✓ | ✓ | ✓ |
| ComparativeReports | ✗ | ✗ | ✓ | ✓ |
| AdvancedReports | ✗ | ✗ | ✓ | ✓ |
| LoyaltyCrm | ✗ | ✗ | ✓ | ✓ |
| CustomerDatabase | ✓ | ✓ | ✓ | ✓ |
| SimpleFolios | ✓ | ✓ | ✓ | ✓ |
| CustomFolios | ✗ | ✗ | ✓ | ✓ |
| AppointmentReminders | ✗ | ✗ | ✓ | ✓ |
| PublicApi | ✗ | ✗ | ✗ | ✓ |
| MultiBranch | ✗ | ✗ | ✗ | ✓ |
| ProviderPayments | ✗ | ✗ | ✓ | ✓ |
| DeliveryPlatforms | ✗ | ✗ | ✓ | ✓ |

\* **Override registrado**: `Basic + QuickService + RealtimeKds = true`. Cafeterías y taquerías obtienen KDS realtime aún en Basic — caso de uso crítico para tiempo de entrega.

### 3.5 Aplicabilidad por MacroCategory (`MacroCategoryFeature`)

| Macro | Features visibles destacadas |
|---|---|
| **Food & Beverage** | + TableMap, WaiterApp, RecipeInventory, TableService, LoyaltyCrm, DeliveryPlatforms |
| **Quick Service** | + KioskMode, KdsBasic/Realtime, DeliveryPlatforms (sin TableMap) |
| **Retail** | + ComparativeReports, MultiWarehouseInventory, StoreCredit. **MaxProducts override = 500** en Free |
| **Services** | + AppointmentReminders. Sin Kitchen/Tables |

### 3.6 Diferenciadores clave (resumen ejecutivo)

- **Free → Basic**: desbloquea **CFDI**, **KDS básico**, **Fiado**.
- **Basic → Pro**: desbloquea **KDS realtime**, **Mapa de mesas**, **Waiter app**, **Multi-warehouse**, **Reportes avanzados**, **Lealtad/CRM**, **Delivery platforms**, **Pagos externos (Clip/MP)**.
- **Pro → Enterprise**: desbloquea **API pública**, **Multi-sucursal (franquicias)**, **Inventario con recetas**.

---

## 4. ENDPOINTS PÚBLICOS

**Total: 35 controllers, 208 endpoints.** Resumen por controller (lista completa con auth y parámetros disponible bajo demanda; aquí sólo el conteo y propósito).

| Controller | # Endpoints | Auth predominante | Propósito |
|---|---|---|---|
| **AuthController** | 5 | Mixto (registro/login públicos) | Registro, email-login, pin-login, switch-branch, /me |
| **BranchController** | 11 | Owner/Manager | CRUD ramas, copy-catalog, PIN management, folio config, settings |
| **BusinessController** | 9 | Owner | Negocio, giro, fiscal, onboarding, features |
| **CashRegisterController** | 10 | Owner/Manager/Cashier | Cajas, sesiones (open/close), movimientos, historial |
| **CatalogController** | 8 | Público | Catálogos de referencia (kitchen statuses, payment methods, plan types, business types, etc.) |
| **CategoriesController** | 6 | Owner / Público (kiosk) | CRUD categorías + endpoint público para kiosk |
| **CustomersController** | 12 | Owner/Manager/Cashier | CRM, búsqueda, fiado, puntos, transacciones, link fiscal |
| **DashboardController** | 1 | Owner/Manager/Cashier | Resumen diario KPI |
| **DeliveryController** | 6 | Mixto (webhook público) | Lifecycle delivery (accept, reject, ready, picked-up) + ingest webhook |
| **DeviceController** | 3 | Mixto | Generate-code (admin), activate, setup |
| **DevicesController** | 6 | Owner/Manager | Registro, heartbeat, validate, list, toggle, update |
| **DiscountPresetController** | 4 | Owner/Cashier | CRUD presets de descuento |
| **FacturapiWebhookController** | 1 | Público (validado por secret) | Webhook eventos Facturapi |
| **HealthController** | 1 | Público | Health check |
| **InventoryController** | 17 | Owner/Manager/Cashier | CRUD items, movimientos (purchase/waste/adjustment), recetas (consumption), ledger, low-stock |
| **InvoicingController** | 6 | Owner/Manager (gated por CfdiInvoicing) | Facturas globales/individuales, descarga PDF/XML, cancelación |
| **OrdersController** | 19 | Owner/Manager/Cashier/Kitchen/Waiter | Sync, pull, lifecycle, payments, intents Clip/MP, move/merge/split, orphans, reconcile |
| **PaymentWebhookController** | 1 | Público (validado por secret) | Webhooks Clip / MercadoPago |
| **PrintJobController** | 5 | Staff | Cola de impresión (kitchen/bar/waiters) |
| **ProductsController** | 15 | Owner/Manager + público (kiosk) | CRUD, búsqueda barcode, stock, import Excel, imágenes Supabase |
| **PromotionController** | 6 | Mixto (cupón público) | CRUD promociones + validate-coupon público |
| **PublicInvoicingController** | 2 | Público (rate-limited) | Portal autoservicio de facturación cliente final |
| **PushController** | 3 | Mixto | VAPID public key, subscribe/unsubscribe push notifications |
| **ReportController** | 6 | Owner (gated por AdvancedReports) | Resumen, export Excel/PDF/CSV, charts BI |
| **ReservationsController** | 9 | Owner/Manager/Host | CRUD reservas + lifecycle (confirm, cancel, no-show, seat) + availability |
| **StockReceiptController** | 3 | Owner/Manager | Recibos de mercancía + procesamiento de movimientos |
| **StripeWebhookController** | 1 | Público (firma Stripe) | Webhook Stripe (subscriptions) |
| **SubscriptionController** | 3 | Owner | Status, checkout, cancel |
| **SupplierController** | 5 | Owner/Manager | CRUD proveedores |
| **TableController** | 6 | Staff | CRUD mesas + status |
| **UserController** | 6 | Owner/Manager | CRUD staff, asignaciones rama, toggle |
| **ZoneController** | 4 | Owner/Manager | CRUD zonas |

**Endpoints públicos sin autenticación** (relevantes para landing/integraciones):
- `POST /api/auth/register` — registro
- `GET /api/catalog/*` (8 endpoints) — catálogos de referencia
- `GET /api/branch/public/{id}`, `GET /api/categories/public`, `GET /api/products/public`, `GET /api/products/public/by-barcode/{code}` — kiosk mode
- `GET /api/promotion/public/active`, `POST /api/promotion/public/validate-coupon` — cupones públicos
- `GET /api/public/invoicing/{orderId}`, `POST /api/public/invoicing/request` — portal autoservicio CFDI
- `POST /api/delivery/webhook/{source}/{branchId}` — UberEats / Rappi / DidiFood
- `POST /api/webhooks/payments/{provider}` — Clip / MercadoPago
- `POST /api/webhooks/facturapi` — Facturapi
- `POST /api/stripe/webhook` — Stripe
- `GET /api/health`, `GET /api/push/vapid-public-key`

---

## 5. FLUJO DE ONBOARDING

### 5.1 Estados (`OnboardingStatusCatalog`)

| Id | Code | Name |
|---|---|---|
| 1 | Pending | Pendiente |
| 2 | InProgress | En progreso |
| 3 | Completed | Completado |
| 4 | Skipped | Omitido |

`Business.CurrentOnboardingStep` (1-based) lleva el paso actual del wizard.

### 5.2 Pasos del wizard

| Paso | Endpoint | Datos capturados |
|---|---|---|
| **1. Registro general** | `POST /api/auth/register` | businessName, ownerName, email, password, primaryMacroCategoryId, planTypeId, folioPrefix, countryCode, timeZoneId. Crea atómicamente: Business, Branch matriz, User owner, UserBranch, Zonas default, Mesa default (si HasTables), Categoría "General", JWT. Email único validado. |
| **2. Giro específico** | `PUT /api/business/giro` | primaryMacroCategoryId (puede actualizarse), subGiroIds[], customGiroDescription. Reemplaza relaciones BusinessGiro. |
| **3. Configuración fiscal** | `PUT /api/business/fiscal` | rfc, taxRegime (código SAT), legalName, invoicingEnabled. Si invoicingEnabled=true sin plan ≥ Basic, retorna **402 Payment Required**. |
| **4. Plan & checkout Stripe** | `POST /api/subscription/checkout` | priceId (whitelist), successUrl, cancelUrl. Crea StripeCustomer si no existe, propaga TrialPeriodDays restantes (≥2 días), retorna URL de Stripe Checkout. |
| **5. Activación dispositivos** | `POST /api/device/generate-code` | branchId, mode (cashier/kiosk/tables/kitchen), name. Genera código de 6 dígitos con TTL ~24h. El terminal lo canjea con `POST /api/device/activate`. |
| **6. Cierre** | `POST /api/business/complete-onboarding` | Marca `OnboardingCompleted=true`, `OnboardingStatusId=3`. Devuelve JWT fresco. |

### 5.3 Conexión con Stripe

**Lifecycle:**
1. Registro → `Business.PlanTypeId` = solicitud (Free por default).
2. Trial en-app: `Business.TrialEndsAt` = +14 días.
3. Checkout → `StripeService.GetOrCreateStripeCustomerAsync()` crea o reusa `StripeCustomerId`.
4. Stripe checkout completado → webhook `checkout.session.completed` entra a **`StripeEventInbox`** (idempotencia por `StripeEventId` único).
5. Background worker procesa inbox → crea/actualiza `Subscription` y sincroniza `Business.PlanTypeId` (denormalizado).

**Estados de Subscription:** `active`, `trialing`, `past_due`, `canceled`, `paused`, `incomplete`, `incomplete_expired`, `unpaid`.
**Billing cycles:** `Monthly`, `Annual`.

### 5.4 Activación de dispositivos (POS terminal enrollment)

| Paso | Quién | Acción |
|---|---|---|
| A | Admin (web) | `POST /device/generate-code` → recibe `code` 6 dígitos |
| B | Terminal | `POST /device/activate` con el code → recibe businessId, branchId, mode, name, features pre-resueltas |
| C | Terminal | `POST /device/setup` con email+password del owner → recibe **device-typed JWT** (lifetime en años, sin userId/roleId, identificado como `tipo=device`) |
| D | Cada request | `IDeviceAuthorizationService` valida `Device.IsActive` (cache TTL 15 min). `DeviceActiveAuthorizationFilter` (HTTP) y `DeviceActiveHubFilter` (SignalR) gatean acceso. |

---

## 6. ORDEN Y PAGOS

### 6.1 Anatomía del `Order`

**Identidad:** Id (UUID 36 chars), OrderNumber, BranchId, UserId.
**Totales:** TotalCents, PaidCents, ChangeCents, SubtotalCents, TaxAmountCents, OrderDiscountCents, TotalDiscountCents.
**Cocina/Mesa:** KitchenStatusId, TableId, TableName.
**Customer (CRM):** CustomerId.
**Invoicing (CFDI):** InvoiceStatus, FacturapiId, InvoiceUrl, InvoicedAt, FiscalCustomerId, InvoiceId.
**Sync (offline):** SyncStatusId, SyncedAt.
**Delivery:** OrderSource (Direct/UberEats/Rappi/DidiFood), ExternalOrderId, DeliveryStatus, DeliveryCustomerName, EstimatedPickupAt.
**Caja:** IsPaid, CashRegisterSessionId.
**Reconciliación (orphans):** IsOrphaned, ReconciliationNote, ReconciledAt, ReconciledBy.
**Cancelación:** CancellationReason, CancelledAt, CancelledBy.
**Promoción:** OrderPromotionId, OrderPromotionName.
**Folio:** FolioNumber.

### 6.2 OrderItem

Quantity, UnitPriceCents, SizeName, ExtrasJson, Notes, DiscountCents, PromotionId, **SatProductCode**, **SatUnitCode**, **TaxRatePercent**, **TaxAmountCents**, **Metadata** (JSON, recién añadido — extensibilidad por vertical, e.g. `BeneficiaryCustomerId` para gym).

### 6.3 OrderPayment & métodos de pago

**Métodos soportados (`PaymentMethod`):**
- **Cash** (efectivo)
- **Card** (tarjeta genérica)
- **Transfer** (transferencia)
- **Other** (otros)
- **Clip** (terminal Clip integrada)
- **MercadoPago** (QR/checkout integrado)
- **BankTerminal** (terminal bancaria genérica no-Clip)
- **StoreCredit** (fiado / saldo a favor)
- **LoyaltyPoints** (puntos canjeados como pago)

**Estados (`PaymentStatusCatalog`):** Pending (1), Completed (2), Failed (3), Refunded (4).

### 6.4 JSON / Metadata flexibles

| Entidad | Campo | Propósito |
|---|---|---|
| **Product** | `Metadata` | Campos nicho por vertical (e.g. gym: `MembershipDurationDays`) |
| **OrderItem** | `Metadata` | Override por línea (e.g. `BeneficiaryCustomerId` para regalos/familiares) |
| **OrderPayment** | `PaymentMetadata` | Datos del proveedor (terminal ID, receipt URL, etc.) |
| **Order** | (ninguno) | No tiene Metadata directo — usa OrderItem.Metadata |
| **Promotion**, **InventoryMovement**, etc. | — | Sin JSON flexible |

### 6.5 Split Payment

✓ **Soportado**. `Order.Payments` es `ICollection<OrderPayment>`, una orden acepta N pagos parciales. Reglas:
- Suma de `AmountCents` ≥ `Order.TotalCents` para marcar `IsPaid=true`.
- `Order.PaidCents` = suma acumulada.
- `Order.ChangeCents = max(0, PaidCents - TotalCents)` (vuelto en efectivo).
- Cada `OrderPayment` puede tener distinto `Method`, `PaymentProvider`, `PaymentStatusId`.

---

## 7. INVENTARIO Y PRODUCTOS

### 7.1 Tipos de productos

| Tipo | Cómo se modela | Caso de uso |
|---|---|---|
| **Simple (con stock)** | `Product.TrackStock=true`, `CurrentStock`, `LowStockThreshold` | Retail, abarrotes, refacciones |
| **Compuesto (con receta)** | `ProductConsumption` enlaza `Product → InventoryItem` con `QuantityPerSale` | Restaurantes (consume ingredientes al vender) |
| **Servicio** | `SatUnitCode = "E48"`, sin TrackStock ni recetas | Estética, gimnasios, consultorios |
| **Membresía (vertical Gym, en construcción)** | `Product.Metadata = {"MembershipDurationDays": 30}`, hook Phase 5e en sync | Gimnasios |

### 7.2 Modificadores

```
Product ──1:N──▶ ProductModifierGroup (e.g. "Salsas", IsRequired, Min/MaxSelectable)
                         │
                         └─1:N──▶ ProductExtra (e.g. "Verde", PriceCents)
```

`OrderItem.ExtrasJson` almacena el array de IDs `ProductExtra` seleccionados al vender. El precio incluye la suma de surcharges.

### 7.3 Control de stock

**Producto simple** (`Product.TrackStock=true`):
SyncEngine deduce `CurrentStock -= Quantity` al vender. Genera `InventoryMovement` (TransactionType=ConsumeFromSale).

**Receta** (`ProductConsumption`):
SyncEngine deduce ingredientes de `InventoryItem` proporcional a `QuantityPerSale × OrderItem.Quantity`.

**Alertas de stock bajo:**
`InventoryService` compara `CurrentStock <= LowStockThreshold`. Endpoints `GET /inventory/low-stock` y `GET /inventory/out-of-stock-products` exponen los items.
Feature gating: `StockAlerts` (FeatureKey 52) — disponible desde plan **Pro**.

### 7.4 Catálogo de movimientos (`InventoryTransactionType`)

Purchase (alta stock), ConsumeFromSale (auto en venta), Waste (merma con razón), ManualAdjustment (ajuste manual), InitialCount (inventario inicial).

**Ledger inmutable:** cada movimiento es persistente; `StockAfterTransaction` snapshot permite reconstruir el ledger.

### 7.5 Recepción de mercancía

`StockReceipt` agrupa items recibidos de un `Supplier`. `POST /api/stock-receipt` crea el receipt y dispara los movimientos `Purchase` automáticamente.

---

## 8. MULTI-TENANT Y SUCURSALES

### 8.1 Modelo de tenancy

```
Business (tenant)
   │
   ├─ N Branches (sucursales) ── PrimaryMacroCategoryId, FolioPrefix, TimeZoneId, HasKitchen/Tables/Delivery
   │       │
   │       └─ 24 entidades scoped a BranchId (IBranchScoped)
   │
   └─ Customers, FiscalCustomers, Subscription, Users, BusinessGiros (compartidos entre ramas)
```

### 8.2 Resolución de `BranchId`

**Origen: JWT claim `branchId`.**

1. Login (`AuthService.GenerateToken`) emite JWT con claims: `branchId`, `businessId`, `userId`, `branches` (JSON de ramas accesibles).
2. `BaseApiController.BranchId` extrae el claim en cada request — lanza `UnauthorizedException` si falta.
3. `POST /api/auth/switch-branch` emite un JWT nuevo con otro `branchId` (el usuario sólo puede cambiar a ramas listadas en `branches`).
4. **`BranchInjectionInterceptor`** (EF Core) sobrescribe `entity.BranchId` con el claim antes de Insert/Update — impide que el cliente especifique BranchId arbitrariamente.

### 8.3 Entidades IBranchScoped (24)

Product, Category, Order, OrderItem (vía Order), CashRegisterSession, CashRegister, Device, DeviceActivationCode, InventoryItem, RestaurantTable, Supplier, StockReceipt, Reservation, Promotion, PromotionUsage, DiscountPreset, Zone, BranchDeliveryConfig, BranchPaymentConfig, PrintJob, KdsEventOutbox, CustomerTransaction, PushSubscription, UserBranch, Invoice.

### 8.4 Entidades por Business (no por Branch)

Customer, FiscalCustomer, Subscription, User, BusinessGiro, Business mismo, AuditLog (transversal).

### 8.5 UserBranch (asignación staff ↔ sucursales)

| Campo | Propósito |
|---|---|
| UserId, BranchId | M:N |
| IsDefault | Marca rama default al login |

Lógica: si el usuario tiene `UserBranch[]`, login resuelve a `IsDefault` o el primero. Si es **Owner sin asignaciones**, accede a TODAS las ramas del negocio automáticamente.

### 8.6 Zone

`Zone` (BranchId, Name, Type [Salon/BarSeats/Other], SortOrder, IsActive) agrupa mesas y dispositivos KDS dentro de una rama. e.g. *Sucursal Centro → Zona Salón → Mesas 1-8*.

---

## 9. HALLAZGOS INESPERADOS

### 9.1 Vertical Gym/Fitness en construcción (~2026-04-25)

Tres migraciones recientes sientan las bases:
- `20260425090358_AddGymVerticalFoundations.cs` — `Product.Metadata`, `Customer.MembershipValidUntil`, `Customer.LastPaymentAt`.
- `20260425094257_AddItemLevelBeneficiary.cs` — `OrderItem.Metadata` para beneficiarios por línea.
- Lógica en `OrderService.SyncOrdersAsync` Phase 5e (`ApplyMembershipExtensionsAsync`): lee `Product.Metadata.MembershipDurationDays` y extiende `Customer.MembershipValidUntil` automáticamente. Soporta padre paga 2 hijos vía `OrderItem.Metadata.BeneficiaryCustomerId`.

**Estado:** fundaciones backend completas, sin UI ni pricing tier dedicado todavía. Sub-giro `Gimnasio / Deportes (Id=20)` ya existe bajo `MacroCategory.Services`.

### 9.2 Documentación interna en `docs/` (31 archivos)

**Series AUDIT-001 → AUDIT-032:** análisis de gaps históricos. Destacan:
- AUDIT-025-device-binding-shift-gating.md
- AUDIT-026-device-auth-vs-user-auth.md

**Series BDD-003 → BDD-015** (designs de features mayores):
- BDD-003 Payment providers (Clip, MercadoPago)
- BDD-004 Public Invoicing API (portal CFDI autoservicio)
- BDD-005 CRM & Loyalty
- BDD-006 Advanced BI Reporting
- BDD-007 Inventory Recipe Management
- BDD-008 Advanced Printing
- BDD-009 KDS Hardware
- BDD-010 KDS SignalR (real-time)
- BDD-011 CRM Store Credit (fiado)
- BDD-012 Session Auth & Roles
- BDD-013 Branch Timezone & Reports
- BDD-014 Device Security & Management
- BDD-015 Settings Matrix Enforcement

**Designs operacionales:**
- design-assign-table-to-order.md
- design-mandatory-cash-session.md

### 9.3 Migrations — historial

**Total: 121 migraciones.** Las 5 más recientes (todas productivas, sin nombres tipo "test"):
1. 20260425094257_AddItemLevelBeneficiary
2. 20260425090358_AddGymVerticalFoundations
3. 20260424101140_AddPlanTypeCatalogPricing
4. 20260420212542_SeedSettingsFeatureKeys
5. 20260420065725_AddNameToActivationCode

### 9.4 Integraciones externas

| Servicio | Propósito | Webhook Inbox |
|---|---|---|
| **Stripe** | Suscripciones SaaS | `StripeEventInbox` |
| **Facturapi** | CFDI 4.0 (México) | (vía controller) |
| **Clip** | Terminal de pagos (México) | `PaymentWebhookInbox` |
| **MercadoPago** | Checkout / QR (LatAm) | `PaymentWebhookInbox` |
| **UberEats / Rappi / DidiFood** | Plataformas de delivery | Endpoint webhook por source |
| **Supabase Storage** | Imágenes de productos | — |
| **Email** (Resend/SendGrid) | Welcome, notifications | — |
| **Web Push (VAPID)** | Notificaciones de navegador | — |

### 9.5 Patrón Inbox para idempotencia

Tres tablas inbox capturan eventos externos antes de procesarlos: `StripeEventInbox`, `PaymentWebhookInbox`, `KdsEventOutbox`. Garantizan idempotencia (unique key sobre el ID externo) y resiliencia ante fallos del worker.

### 9.6 Cobertura del repositorio

- **35 controllers** con cobertura completa por entidad — sin entidades huérfanas.
- **0 TODOs / FIXMEs / HACKs** detectados en `POS.API/Controllers/` y `POS.Services/`. Código limpio.
- Todas las entidades tienen repositorio o acceso vía `IUnitOfWork`.

### 9.7 Validación cross-tenant

`BranchInjectionInterceptor` impide overrides maliciosos de `BranchId` en mutaciones. La nueva lógica de membresías valida `Customer.BusinessId == Branch.BusinessId` para impedir ataques cross-tenant en regalos de membresía.

---

## Apéndice — Stack y arquitectura

| Capa | Tecnología | Responsabilidad |
|---|---|---|
| **API** | ASP.NET Core 10, JWT Bearer, Swagger | Controllers, middleware, routing |
| **Domain** | C# 13 records/classes | Models, enums, exceptions, settings |
| **Repository** | EF Core 10, PostgreSQL (Npgsql), 121 migraciones | DbContext, Generic Repository, UnitOfWork |
| **Services** | Inyección de dependencias, Async/await | Business logic, integraciones externas |
| **Auth** | JWT con claim `branchId`, `businessId` + Device tokens | Multi-tenant via interceptor |
| **Real-time** | SignalR (KDS Hub con DeviceActiveHubFilter) | Eventos de cocina en vivo |
| **Logging** | Serilog | Structured logging |
| **Hosted en** | Render (`render.yaml` + Dockerfile) | Container deployment |

---

**Reporte completo. 9 secciones, sin modificaciones al código. Listo para diseño/marketing.**
