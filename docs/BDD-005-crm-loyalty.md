# BDD-005 — Customer Relationship Management (CRM) & Loyalty

**Fecha:** 2026-04-03
**Fase:** 16 — CRM, Store Credit & Loyalty Points
**Autor:** Arquitecto Senior .NET
**Estado:** Pendiente de aprobacion

---

## 1. Resumen Ejecutivo

El sistema POS actualmente no tiene un modelo unificado de "Cliente". Los datos de cliente estan fragmentados en tres lugares disjuntos:
- **Reservation.GuestName / GuestPhone** — texto libre, no reutilizable.
- **FiscalCustomer** — datos fiscales para facturacion, scoped a Business.
- **Order.DeliveryCustomerName** — texto de plataformas de delivery.

Este documento disena un modelo `Customer` centralizado que unifica estos contextos y habilita **Fiado (Store Credit)** y **Puntos de Lealtad**, dos features criticos para negocios recurrentes en Mexico (taquerias, fondas, abarrotes).

---

## 2. Hallazgos del Analisis

### 2.1 Estado actual — Fragmentacion de datos de cliente

| Fuente | Modelo | Tipo | Reutilizable? | Vinculo a Order? |
|--------|--------|------|---------------|-----------------|
| Reservacion | `Reservation.GuestName` + `GuestPhone` | Texto libre | NO — se pierde al crear la siguiente | Solo via mesa |
| Facturacion | `FiscalCustomer.Rfc` + `BusinessName` | Entidad con FK | SI — `Order.FiscalCustomerId` | SI (FK directa) |
| Delivery | `Order.DeliveryCustomerName` | Texto en la orden | NO — diferente por plataforma | Inline en orden |

**Ningun modelo permite:** "Mostrar historial de compras de Juan", "Juan tiene $200 de fiado", "Juan tiene 150 puntos de lealtad".

### 2.2 Reservation — No tiene FK a un cliente

`Reservation` tiene `GuestName` (string) y `GuestPhone` (string?) pero NO tiene un `CustomerId`. Si "Juan Perez" reserva 3 veces, son 3 strings independientes. No hay forma de vincularlos ni ver su historial.

### 2.3 PaymentMethod Enum — Estado actual

```
Cash (0), Card (1), Transfer (2), Other (3), Clip (4), MercadoPago (5), BankTerminal (6)
```

No existe `StoreCredit` ni `LoyaltyPoints`.

### 2.4 FiscalCustomer — Relacion con Customer

`FiscalCustomer` vive en scope de `Business` (unique por `BusinessId + Rfc`). Un cliente comercial puede tener datos fiscales, pero no todo cliente tiene RFC. La relacion debe ser **1:1 opcional** — un `Customer` puede tener cero o un `FiscalCustomer`.

### 2.5 Modelos de fiado en Mexico

El "fiado" (credito a clientes de confianza) es una practica comun en:
- Tiendas de abarrotes
- Fondas / cocinas economicas
- Papelerias
- Tortillerias

El patron es: el cliente acumula deuda (credito consumido), y periódicamente paga (abono). El negocio necesita: saldo actual, historial de movimientos, limite de credito opcional.

---

## 3. Decisiones Arquitecturales

### 3.1 Scope del Customer — Business vs. Branch

**Decision: `Customer` pertenece a `Business`** (no a Branch).

**Justificacion:** Un cliente fiel visita multiples sucursales del mismo negocio. Su saldo de credito y puntos deben ser cross-branch. "Juan tiene $500 de fiado en Taqueria El Sol" aplica a cualquier sucursal de El Sol.

### 3.2 Customer vs. FiscalCustomer — Relacion

**Decision: `FiscalCustomer` tiene un FK opcional `CustomerId`** (no al reves).

**Justificacion:**
- No todo `Customer` tiene datos fiscales (la mayoria no).
- No todo `FiscalCustomer` necesita ser un `Customer` del CRM (una empresa que pide una factura unica no es un "cliente recurrente").
- Agregar `CustomerId` a `FiscalCustomer` permite vincularlos cuando coinciden, sin forzar la relacion.

### 3.3 Ledger de transacciones — Patron Double-Entry vs. Simple Log

**Decision: Ledger simple (single-entry) con tipo de movimiento.**

Un sistema double-entry (debito/credito) es overkill para un POS. Un ledger simple con `TransactionType` (credit_added, credit_used, points_earned, points_redeemed) es suficiente y mucho mas facil de consultar.

**Invariante critica:** `Customer.CreditBalanceCents` y `Customer.PointsBalance` son campos denormalizados. El ledger (`CustomerTransaction`) es la fuente de verdad. Si hay discrepancia, el ledger gana. Un job de reconciliacion puede recalcular los balances.

### 3.4 Puntos de lealtad — Acumulacion

**Decision: Configurable por negocio en `Business`.**

Ejemplo: "1 punto por cada $10 MXN gastados". La configuracion vive en `Business` como `PointsPerCurrencyUnit` (int) y `CurrencyUnitsPerPoint` (int). Default: 1 punto por cada $10 = `PointsPerCurrencyUnit = 1, CurrencyUnitsPerPoint = 1000` (10 pesos = 1000 centavos).

### 3.5 Pagos con Store Credit / Loyalty Points

**Decision: Nuevos valores en `PaymentMethod` enum + validacion en `OrderService`.**

Cuando se usa `StoreCredit` como metodo de pago:
1. El `OrderPayment` tiene `Method = StoreCredit`.
2. El `OrderService.AddPaymentAsync` (o `SyncOrdersAsync`) valida que el `Customer` tiene saldo suficiente.
3. Se crea un `CustomerTransaction` tipo `credit_used`.
4. Se decrementa `Customer.CreditBalanceCents`.

Mismo patron para `LoyaltyPoints`, con una tasa de conversion configurable (e.g., 100 puntos = $10 MXN).

### 3.6 Offline-first — Credit/Points en el POS

**Decision: El POS frontend cachea el saldo del cliente.** Al sincronizar, el backend valida y ajusta. Si el saldo offline diverge del backend (e.g., otro dispositivo uso credito), el backend rechaza el pago con un error especifico `INSUFFICIENT_CREDIT` o `INSUFFICIENT_POINTS`.

---

## 4. Modelos de Dominio

### 4.1 Customer — Nuevo modelo

| Propiedad | Tipo | MaxLength | Default | Descripcion |
|-----------|------|-----------|---------|-------------|
| `Id` | `int` | — | PK | Identificador unico |
| `BusinessId` | `int` | — | FK | Negocio al que pertenece el cliente |
| `FirstName` | `string` | 100 | Required | Nombre del cliente |
| `LastName` | `string?` | 100 | null | Apellido(s) |
| `Phone` | `string?` | 20 | null | Telefono (unico por business si no null) |
| `Email` | `string?` | 255 | null | Email |
| `CreditBalanceCents` | `int` | — | 0 | Saldo de credito (fiado) en centavos. Positivo = el negocio le debe al cliente (prepago). Negativo = el cliente debe al negocio (fiado). |
| `CreditLimitCents` | `int` | — | 0 | Limite maximo de fiado en centavos. 0 = sin limite (confianza total). |
| `PointsBalance` | `int` | — | 0 | Puntos de lealtad acumulados |
| `Notes` | `string?` | 500 | null | Notas internas del negocio sobre el cliente |
| `IsActive` | `bool` | — | true | Soft delete / desactivacion |
| `CreatedAt` | `DateTime` | — | UtcNow | Creacion |
| `UpdatedAt` | `DateTime?` | — | null | Ultima actualizacion |

**Navegacion:**
- `Business` — FK a Business
- `Orders` — ICollection (via `Order.CustomerId`)
- `Reservations` — ICollection (via `Reservation.CustomerId`)
- `Transactions` — ICollection (via `CustomerTransaction.CustomerId`)

**Indices:**
- `(BusinessId, Phone)` — unique filtrado WHERE Phone IS NOT NULL
- `(BusinessId, LastName, FirstName)` — para busqueda por nombre
- `BusinessId` — para listar clientes de un negocio

### 4.2 CustomerTransaction — Nuevo modelo (Ledger)

| Propiedad | Tipo | MaxLength | Descripcion |
|-----------|------|-----------|-------------|
| `Id` | `int` | — | PK |
| `CustomerId` | `int` | — | FK a Customer |
| `BranchId` | `int` | — | FK a Branch (donde ocurrio la transaccion) |
| `Type` | `string` | 30 | Tipo de movimiento (ver tabla abajo) |
| `AmountCents` | `int` | — | Monto en centavos (positivo = a favor del cliente, negativo = consumo) |
| `PointsAmount` | `int` | — | Puntos (positivo = ganados, negativo = redimidos) |
| `BalanceAfterCents` | `int` | — | Saldo de credito despues de la transaccion (snapshot) |
| `PointsBalanceAfter` | `int` | — | Saldo de puntos despues de la transaccion (snapshot) |
| `OrderId` | `string?` | 36 | FK a Order (si aplica). Null para ajustes manuales. |
| `Description` | `string` | 200 | Descripcion del movimiento (e.g., "Fiado - Orden #42") |
| `CreatedBy` | `string` | 100 | Usuario que registro el movimiento |
| `CreatedAt` | `DateTime` | — | Timestamp |

**Tipos de transaccion (`Type`):**

| Constante | Valor | Descripcion |
|-----------|-------|-------------|
| `CreditAdded` | `"credit_added"` | Abono de credito (cliente paga deuda) |
| `CreditUsed` | `"credit_used"` | Consumo de credito (fiado) |
| `CreditAdjustment` | `"credit_adjustment"` | Ajuste manual por el Owner |
| `PointsEarned` | `"points_earned"` | Puntos ganados por compra |
| `PointsRedeemed` | `"points_redeemed"` | Puntos canjeados como pago |
| `PointsAdjustment` | `"points_adjustment"` | Ajuste manual de puntos |

**Archivo helper:** `POS.Domain/Helpers/CustomerTransactionType.cs`

### 4.3 Order — Campo nuevo

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `CustomerId` | `int?` | FK nullable a Customer. Null para ventas anonimas (la mayoria). |

Navegacion: `Customer?`

### 4.4 Reservation — Campo nuevo

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `CustomerId` | `int?` | FK nullable a Customer. Null para reservaciones sin cliente registrado (backward compatible). |

Navegacion: `Customer?`

### 4.5 FiscalCustomer — Campo nuevo

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `CustomerId` | `int?` | FK nullable a Customer. Vincula datos fiscales con el perfil CRM. |

Navegacion: `Customer?`

### 4.6 PaymentMethod Enum — Valores nuevos

```
Cash (0), Card (1), Transfer (2), Other (3), Clip (4), MercadoPago (5),
BankTerminal (6), StoreCredit (7), LoyaltyPoints (8)
```

### 4.7 Business — Campos nuevos (Loyalty Config)

| Propiedad | Tipo | Default | Descripcion |
|-----------|------|---------|-------------|
| `LoyaltyEnabled` | `bool` | false | Si el programa de lealtad esta activo |
| `PointsPerCurrencyUnit` | `int` | 1 | Puntos otorgados por cada unidad de moneda |
| `CurrencyUnitsPerPoint` | `int` | 1000 | Centavos necesarios para ganar los puntos (1000 = $10 MXN) |
| `PointRedemptionValueCents` | `int` | 10 | Valor en centavos de cada punto al redimir (10 = $0.10 MXN) |

**Ejemplo:** Con defaults, por cada $10 MXN gastados el cliente gana 1 punto. Cada punto vale $0.10 MXN. Para obtener $10 MXN de descuento, necesita 100 puntos (= $1,000 MXN en compras).

### 4.8 SyncPaymentRequest — Campo nuevo

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `CustomerId` | `int?` | ID del cliente cuando se usa StoreCredit o LoyaltyPoints |

### 4.9 SyncOrderRequest — Campo nuevo

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `CustomerId` | `int?` | ID del cliente asociado a la orden |

---

## 5. Servicios

### 5.1 ICustomerService

| Metodo | Descripcion |
|--------|-------------|
| `GetByIdAsync(int id)` | Obtiene un cliente con su historial de transacciones |
| `GetByBusinessAsync(int businessId)` | Lista todos los clientes del negocio |
| `SearchAsync(int businessId, string query)` | Busca por nombre, telefono, o email |
| `CreateAsync(int businessId, Customer customer)` | Crea un nuevo cliente |
| `UpdateAsync(int id, Customer customer)` | Actualiza datos del cliente |
| `DeactivateAsync(int id)` | Soft delete (IsActive = false) |
| `AddCreditAsync(int customerId, int amountCents, string description, int branchId, string createdBy)` | Agrega credito (abono del cliente). Crea CustomerTransaction. |
| `UseCreditAsync(int customerId, int amountCents, string orderId, int branchId, string createdBy)` | Consume credito (fiado). Valida limite. Crea CustomerTransaction. |
| `EarnPointsAsync(int customerId, int orderTotalCents, string orderId, int branchId, string createdBy)` | Calcula y otorga puntos por una compra. Crea CustomerTransaction. |
| `RedeemPointsAsync(int customerId, int points, string orderId, int branchId, string createdBy)` | Canjea puntos como pago. Valida saldo. Crea CustomerTransaction. |
| `GetTransactionsAsync(int customerId, DateTime? from, DateTime? to)` | Historial de transacciones con filtro de fecha |
| `RecalculateBalancesAsync(int customerId)` | Reconciliacion: recalcula saldos desde el ledger |
| `LinkFiscalCustomerAsync(int customerId, int fiscalCustomerId)` | Vincula un Customer con un FiscalCustomer existente |

### 5.2 Validaciones criticas

**UseCreditAsync:**
1. `Customer.IsActive == true`
2. Si `CreditLimitCents > 0`: `abs(CreditBalanceCents - amountCents) <= CreditLimitCents`
3. Atomicidad: transaction DB para actualizar balance + crear ledger entry

**RedeemPointsAsync:**
1. `Customer.PointsBalance >= points`
2. `Business.LoyaltyEnabled == true`
3. Atomicidad: transaction DB

---

## 6. API Endpoints

### 6.1 Customer CRUD

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `GET /api/customers` | GET | Owner, Manager, Cashier | Lista clientes del negocio |
| `GET /api/customers/search?q={query}` | GET | Owner, Manager, Cashier | Busca por nombre/telefono/email |
| `GET /api/customers/{id}` | GET | Owner, Manager, Cashier | Detalle del cliente con saldos |
| `POST /api/customers` | POST | Owner, Manager | Crea un nuevo cliente |
| `PUT /api/customers/{id}` | PUT | Owner, Manager | Actualiza datos del cliente |
| `DELETE /api/customers/{id}` | DELETE | Owner | Desactiva el cliente |

### 6.2 Credit (Fiado)

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `POST /api/customers/{id}/credit/add` | POST | Owner, Manager | Registra abono de credito |
| `POST /api/customers/{id}/credit/adjust` | POST | Owner | Ajuste manual de saldo |

### 6.3 Loyalty Points

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `POST /api/customers/{id}/points/adjust` | POST | Owner | Ajuste manual de puntos |
| `GET /api/customers/{id}/transactions` | GET | Owner, Manager | Historial de movimientos |

### 6.4 Fiscal Link

| Endpoint | Metodo | Auth | Descripcion |
|----------|--------|------|-------------|
| `POST /api/customers/{id}/link-fiscal` | POST | Owner, Manager | Vincula con FiscalCustomer |

**Nota:** Los endpoints de `credit/use` y `points/redeem` NO son endpoints directos — ocurren automaticamente dentro del Sync Engine cuando un `OrderPayment` usa `StoreCredit` o `LoyaltyPoints`.

---

## 7. Impacto en el Sync Engine

### 7.1 SyncOrdersAsync — Flujo extendido

Cuando un `SyncPaymentRequest` tiene `Method = "StoreCredit"` o `"LoyaltyPoints"`:

```
Phase 2 (Classify):
  - Detectar pagos con StoreCredit/LoyaltyPoints
  - Requiere CustomerId en el SyncPaymentRequest

Phase 2c (NEW — Validate Customer Payments):
  - Para cada pago StoreCredit:
    a. Cargar Customer por CustomerId
    b. Validar IsActive
    c. Validar saldo suficiente o dentro de CreditLimit
    d. Si falla → ValidationException("INSUFFICIENT_CREDIT: ...")
  - Para cada pago LoyaltyPoints:
    a. Cargar Customer por CustomerId
    b. Validar Business.LoyaltyEnabled
    c. Calcular centavos equivalentes (points * PointRedemptionValueCents)
    d. Validar saldo de puntos suficiente
    e. Si falla → ValidationException("INSUFFICIENT_POINTS: ...")

Phase 3 (Persist):
  - SaveChangesAsync incluye las ordenes con CustomerId

Phase 5c (NEW — Process Customer Transactions):
  - Para cada pago StoreCredit completado:
    a. Customer.CreditBalanceCents -= amountCents
    b. Crear CustomerTransaction(credit_used)
  - Para cada pago LoyaltyPoints completado:
    a. Calcular puntos consumidos
    b. Customer.PointsBalance -= points
    c. Crear CustomerTransaction(points_redeemed)
  - Para cada orden con CustomerId + Business.LoyaltyEnabled:
    a. Calcular puntos ganados por el total de la orden
    b. Customer.PointsBalance += earnedPoints
    c. Crear CustomerTransaction(points_earned)
  - SaveChangesAsync
```

### 7.2 Backward Compatibility

- `SyncOrderRequest.CustomerId` nullable — ordenes sin cliente siguen funcionando.
- `SyncPaymentRequest.CustomerId` nullable — pagos sin cliente (Cash, Card, etc.) sin cambios.
- Los nuevos `PaymentMethod` values (`StoreCredit`, `LoyaltyPoints`) requieren `CustomerId` — si falta, el `MapToPayment` fallara con un error descriptivo.

---

## 8. Impacto en CashRegisterSession

`CalculateCashSalesAsync` filtra por `PaymentMethod.Cash` — no se ve afectado.

Sin embargo, para el reporte de cierre de caja, seria util agregar un desglose por `StoreCredit` y `LoyaltyPoints`. **Esto es scope separado** — no afecta la implementacion del modelo.

---

## 9. Schema: Tablas y Migrations

### 9.1 Customer — Tabla nueva

| Columna | Tipo SQL | Nullable | Indice |
|---------|----------|----------|--------|
| `Id` | `serial` | NO (PK) | — |
| `BusinessId` | `integer` | NO (FK) | `(BusinessId, Phone)` UNIQUE filtrado |
| `FirstName` | `varchar(100)` | NO | `(BusinessId, LastName, FirstName)` |
| `LastName` | `varchar(100)` | YES | (incluido en indice compuesto) |
| `Phone` | `varchar(20)` | YES | (incluido en unique filtrado) |
| `Email` | `varchar(255)` | YES | — |
| `CreditBalanceCents` | `integer` | NO | default 0 |
| `CreditLimitCents` | `integer` | NO | default 0 |
| `PointsBalance` | `integer` | NO | default 0 |
| `Notes` | `varchar(500)` | YES | — |
| `IsActive` | `boolean` | NO | default true |
| `CreatedAt` | `timestamp` | NO | — |
| `UpdatedAt` | `timestamp` | YES | — |

### 9.2 CustomerTransaction — Tabla nueva

| Columna | Tipo SQL | Nullable | Indice |
|---------|----------|----------|--------|
| `Id` | `serial` | NO (PK) | — |
| `CustomerId` | `integer` | NO (FK) | YES |
| `BranchId` | `integer` | NO (FK) | — |
| `Type` | `varchar(30)` | NO | — |
| `AmountCents` | `integer` | NO | — |
| `PointsAmount` | `integer` | NO | default 0 |
| `BalanceAfterCents` | `integer` | NO | — |
| `PointsBalanceAfter` | `integer` | NO | — |
| `OrderId` | `varchar(36)` | YES (FK) | — |
| `Description` | `varchar(200)` | NO | — |
| `CreatedBy` | `varchar(100)` | NO | — |
| `CreatedAt` | `timestamp` | NO | `(CustomerId, CreatedAt)` |

### 9.3 Order — Columna nueva

| Columna | Tipo SQL | Nullable | Indice |
|---------|----------|----------|--------|
| `CustomerId` | `integer` | YES (FK) | YES |

### 9.4 Reservation — Columna nueva

| Columna | Tipo SQL | Nullable |
|---------|----------|----------|
| `CustomerId` | `integer` | YES (FK) |

### 9.5 FiscalCustomer — Columna nueva

| Columna | Tipo SQL | Nullable | Indice |
|---------|----------|----------|--------|
| `CustomerId` | `integer` | YES (FK) | YES (unique) |

### 9.6 Business — Columnas nuevas

| Columna | Tipo SQL | Default |
|---------|----------|---------|
| `LoyaltyEnabled` | `boolean` | false |
| `PointsPerCurrencyUnit` | `integer` | 1 |
| `CurrencyUnitsPerPoint` | `integer` | 1000 |
| `PointRedemptionValueCents` | `integer` | 10 |

---

## 10. Cambios Requeridos por Capa

### 10.1 POS.Domain

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `Models/Customer.cs` | **CREAR** | Modelo completo con saldos y relaciones |
| `Models/CustomerTransaction.cs` | **CREAR** | Ledger de movimientos |
| `Models/Order.cs` | **MODIFICAR** | +`CustomerId` (int?) + navegacion |
| `Models/Reservation.cs` | **MODIFICAR** | +`CustomerId` (int?) + navegacion |
| `Models/FiscalCustomer.cs` | **MODIFICAR** | +`CustomerId` (int?) + navegacion |
| `Models/Business.cs` | **MODIFICAR** | +4 campos de loyalty config |
| `Models/SyncOrderRequest.cs` | **MODIFICAR** | +`CustomerId` en request y payment |
| `Enums/PaymentMethod.cs` | **MODIFICAR** | +`StoreCredit (7)`, `LoyaltyPoints (8)` |
| `Helpers/CustomerTransactionType.cs` | **CREAR** | Constantes string para tipos de transaccion |

### 10.2 POS.Repository

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `ApplicationDbContext.cs` | **MODIFICAR** | +2 DbSets, config FK/indices para Customer, CustomerTransaction, y FKs nuevas |
| `IRepository/ICustomerRepository.cs` | **CREAR** | `GetByBusinessAsync`, `SearchAsync`, `GetByPhoneAsync` |
| `Repository/CustomerRepository.cs` | **CREAR** | Implementacion con busqueda por nombre/telefono |
| `IRepository/ICustomerTransactionRepository.cs` | **CREAR** | `GetByCustomerAsync(id, from?, to?)` |
| `Repository/CustomerTransactionRepository.cs` | **CREAR** | Implementacion |
| `IUnitOfWork.cs` | **MODIFICAR** | +2 repos |
| `UnitOfWork.cs` | **MODIFICAR** | Lazy init |
| `Migrations/` | **CREAR** | Migration masiva |

### 10.3 POS.Services

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `IService/ICustomerService.cs` | **CREAR** | Interface completa (13 metodos) |
| `Service/CustomerService.cs` | **CREAR** | Implementacion con transacciones atomicas |
| `Service/OrderService.cs` | **MODIFICAR** | Phase 2c (validacion) + Phase 5c (transacciones) + MapToOrder/MapToPayment |
| `Dependencies/ServiceDependencies.cs` | **MODIFICAR** | +ICustomerService |

### 10.4 POS.API

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `Controllers/CustomerController.cs` | **CREAR** | CRUD + credit + points + transactions + fiscal link |

---

## 11. Orden de Implementacion Sugerido

| Subfase | Descripcion | Dependencias |
|---------|-------------|--------------|
| **16a** | Customer + CustomerTransaction models + PaymentMethod enum + Business loyalty fields + Order/Reservation/FiscalCustomer FKs + migration | Ninguna |
| **16b** | ICustomerRepository + ICustomerTransactionRepository + UoW + CustomerController (CRUD) | 16a |
| **16c** | ICustomerService (credit operations) + endpoints de fiado + ajuste manual | 16a, 16b |
| **16d** | Loyalty points (earn/redeem) + Business loyalty config endpoints | 16c |
| **16e** | Sync Engine integration (Phase 2c + 5c) + SyncOrderRequest/SyncPaymentRequest campos | 16c, 16d |
| **16f** | FiscalCustomer link + Reservation CustomerId integration | 16b |

**Phase 16a es el foundation** — schema y modelos. Se puede implementar independientemente.

---

## 12. Backward Compatibility

- Todas las FKs nuevas son **nullable** — datos existentes intactos.
- `PaymentMethod` enum usa `HasConversion<string>()` — nuevos valores se almacenan como strings.
- `Order.CustomerId = null` para todas las ordenes existentes.
- `Reservation.CustomerId = null` para todas las reservaciones existentes.
- `FiscalCustomer.CustomerId = null` para todos los clientes fiscales existentes.
- `Business.LoyaltyEnabled = false` — programa de lealtad deshabilitado por default.
- `CalculateCashSalesAsync` no se ve afectado (filtra `PaymentMethod.Cash`).
- **Zero breaking changes** para frontends que no usen CRM.

---

## 13. Lo que NO esta en scope

- App movil de lealtad para el cliente (solo manejo desde el POS).
- Tarjetas de lealtad fisicas / QR de cliente.
- Campanas de marketing (emails/SMS a clientes).
- Merge de duplicados de clientes.
- Import masivo de clientes desde Excel.
- Reporte de cartera vencida (ageing report) — scope de reportes.
- Notificaciones push cuando el credito esta por llegar al limite.
- Conversion de puntos entre negocios (puntos son per-business).
