# AUDIT-005: Gap Analysis & Design — Multi-Till Support (Individual Cash Registers)

**Fecha:** 2026-04-06
**Auditor:** Claude Code
**Estado:** Pendiente de aprobacion

---

## 1. Resumen Ejecutivo

Actualmente, `CashRegisterSession` esta vinculada unicamente a un `BranchId`. Un **filtered unique index** garantiza que solo exista una sesion abierta por branch (`"Status" = 'open'`). Este modelo "Shared Till" funciona para food trucks y restaurantes pequenos, pero no escala a supermercados/retail donde multiples cajas fisicas operan simultaneamente.

Este documento disena la transicion a un modelo **Multi-Till**: cada branch puede tener N registros fisicos (`CashRegister`), cada uno con su propia sesion de caja independiente.

---

## 2. Analisis del Estado Actual

### 2.1 Constraint critico: una sesion abierta por branch

```
// ApplicationDbContext.cs — CashRegisterSession config
entity.HasIndex(s => s.BranchId)
    .IsUnique()
    .HasFilter("\"Status\" = 'open'");
```

Este indice **impide fisicamente** abrir dos sesiones simultaneas en el mismo branch. Es la barrera principal a eliminar.

### 2.2 Todas las operaciones asumen "1 sesion por branch"

| Archivo | Metodo | Patron actual | Problema Multi-Till |
|---------|--------|---------------|---------------------|
| `CashRegisterService.cs` | `GetOpenSessionAsync(branchId)` | Busca LA sesion abierta del branch | Con multi-till habra N sesiones abiertas; necesita filtrar por register |
| `CashRegisterService.cs` | `OpenSessionAsync(branchId, req)` | Valida que no haya sesion abierta en el branch | Debe validar por register, no por branch |
| `CashRegisterService.cs` | `CloseSessionAsync(branchId, req)` | Cierra LA sesion del branch | Debe cerrar la sesion de un register especifico |
| `CashRegisterService.cs` | `AddMovementAsync(branchId, req)` | Agrega movimiento a LA sesion del branch | Debe agregar al register especifico |
| `CashRegisterSessionRepository.cs` | `GetOpenSessionAsync(branchId)` | `WHERE BranchId = @id AND Status = 'open'` | Retornaria multiples resultados; `FirstOrDefault` seria arbitrario |
| `OrderService.cs` | Phase 1b validation | Valida que el `CashRegisterSessionId` del request pertenezca al branch y este abierta | Ya funciona correctamente — valida por session ID, no por branch |
| `OrderService.cs` | `AddPaymentAsync` | Valida que la sesion asociada a la orden este abierta | Ya funciona correctamente — valida por session ID |
| `CashRegisterController.cs` | Todos los endpoints | Pasa solo `BranchId` al service | Necesita pasar tambien `registerId` |

### 2.3 Lo que YA funciona con Multi-Till (sin cambios)

- **`OrderService.SyncOrdersAsync`**: Phase 1b valida el `CashRegisterSessionId` por ID directo, no por branch. Seguira funcionando porque cada orden ya viene vinculada a una sesion especifica.
- **`OrderService.AddPaymentAsync`**: Valida `order.CashRegisterSessionId` por ID directo.
- **`CalculateCashSalesAsync`**: Calcula por ventana temporal + branchId. Con multi-till esto ya no sera preciso (ver seccion 7).
- **`Order.CashRegisterSessionId` FK**: Ya es nullable int, no necesita cambios.

### 2.4 Entidades existentes que NO colisionan

- `DeviceActivationCode`: Es para licenciamiento/activacion de dispositivos, no para hardware de caja. El nuevo `CashRegister` es conceptualmente diferente. Sin colision de nombres.

---

## 3. Nueva Entidad: `CashRegister`

### 3.1 Por que "CashRegister" y no "PhysicalRegister" o "Device"

- `Device` ya existe conceptualmente en el contexto de `DeviceActivationCode`. Reutilizar el nombre causaria confusion.
- `PhysicalRegister` es demasiado largo y no agrega claridad.
- `CashRegister` es el termino del dominio en retail/POS: "caja registradora". Ademas, se alinea con `CashRegisterSession` y `CashRegisterService`.

### 3.2 Modelo de dominio

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `Id` | `int` | PK auto-increment |
| `BranchId` | `int` | FK a Branch |
| `Name` | `string (max 50)` | Nombre visible: "Caja 1", "Caja 2" |
| `DeviceUuid` | `string? (max 100)` | UUID del navegador/dispositivo vinculado. Nullable hasta que un dispositivo se vincule. Unico por branch. |
| `IsActive` | `bool` | Soft-delete / deshabilitacion |
| `CreatedAt` | `DateTime` | Timestamp de creacion |
| `Branch` | `virtual Branch?` | Navegacion |
| `Sessions` | `virtual ICollection<CashRegisterSession>?` | Navegacion inversa |

### 3.3 Reglas de negocio

1. **Nombre unico por branch**: No puede haber dos cajas con el mismo nombre en un branch.
2. **DeviceUuid unico por branch**: Un dispositivo solo puede estar vinculado a una caja por branch.
3. **DeviceUuid es opcional**: Un CashRegister puede existir sin dispositivo vinculado (configuracion previa al uso). Tambien permite re-vincular si un dispositivo se dania.
4. **No se elimina**: Solo se desactiva (`IsActive = false`). Las sesiones historicas mantienen la referencia.

---

## 4. Cambios en `CashRegisterSession`

### 4.1 Nueva propiedad FK

| Propiedad | Tipo | Descripcion |
|-----------|------|-------------|
| `CashRegisterId` | `int?` | FK nullable a `CashRegister`. Nullable para sesiones historicas pre-migration y para backwards compatibility temporal. |
| `CashRegister` | `virtual CashRegister?` | Navegacion |

### 4.2 Evolucion del unique index

**Actual:**
```
UNIQUE WHERE "Status" = 'open' ON (BranchId)
→ 1 sesion abierta por branch
```

**Nuevo:**
```
UNIQUE WHERE "Status" = 'open' ON (CashRegisterId)
→ 1 sesion abierta por caja fisica
```

**Nota critica:** El index actual en `BranchId` debe ser **reemplazado**, no complementado. Si se mantienen ambos, el index viejo seguiria bloqueando multiples sesiones abiertas por branch.

### 4.3 Periodo de transicion (backwards compat)

Durante la migration, las sesiones existentes tendran `CashRegisterId = NULL`. Esto es correcto:
- Las sesiones historicas cerradas no necesitan register.
- Si hay una sesion **abierta** al momento de la migration, se debe decidir: (a) cerrarla forzosamente, o (b) crear un `CashRegister` default "Caja Principal" y asignarlo. **Recomendacion: opcion (b)** — crear un register default por branch y asignar sesiones abiertas existentes.

---

## 5. Schema Changes & EF Core Configuration

### 5.1 Nuevo archivo: `POS.Domain/Models/CashRegister.cs`

```
CashRegister
├── Id (int, PK)
├── BranchId (int, FK)
├── Name (string, max 50, required)
├── DeviceUuid (string?, max 100)
├── IsActive (bool, default true)
├── CreatedAt (DateTime)
├── Branch (virtual navigation)
└── Sessions (virtual ICollection<CashRegisterSession>?)
```

### 5.2 Modificacion: `POS.Domain/Models/CashRegisterSession.cs`

```
+ CashRegisterId (int?, FK)
+ CashRegister (virtual navigation)
```

### 5.3 Modificacion: `POS.Domain/Models/Branch.cs`

```
+ CashRegisters (virtual ICollection<CashRegister>?)  // navegacion inversa
```

### 5.4 Configuracion en `ApplicationDbContext.cs`

**Nuevo bloque — CashRegister:**
```
entity CashRegister:
  - HasOne(Branch).WithMany(CashRegisters).FK(BranchId)
  - HasMany(Sessions).WithOne(CashRegister).FK(CashRegisterId).OnDelete(Restrict)
  - Index UNIQUE(BranchId, Name) — nombre unico por branch
  - Index UNIQUE(BranchId, DeviceUuid).HasFilter("DeviceUuid" IS NOT NULL) — uuid unico por branch
  - Property(Name).HasMaxLength(50).IsRequired()
  - Property(DeviceUuid).HasMaxLength(100)
```

**Modificar bloque — CashRegisterSession:**
```
ELIMINAR:
  - HasIndex(BranchId).IsUnique().HasFilter("Status" = 'open')

AGREGAR:
  - HasOne(CashRegister).WithMany(Sessions).FK(CashRegisterId).IsRequired(false).OnDelete(Restrict)
  - HasIndex(CashRegisterId).IsUnique().HasFilter("Status" = 'open' AND "CashRegisterId" IS NOT NULL)
  - Index en CashRegisterId (no-unique, para lookups)
```

**OnDelete(Restrict)**: No se permite eliminar un CashRegister si tiene sesiones. Se debe desactivar (`IsActive = false`).

### 5.5 Migration

```bash
dotnet ef migrations add AddCashRegisterMultiTill --project POS.Repository --startup-project POS.API
```

**Migration data seed:**
1. Para cada branch que tenga al menos una `CashRegisterSession`:
   - INSERT un `CashRegister` default con `Name = 'Caja Principal'`, `IsActive = true`.
   - UPDATE todas las `CashRegisterSessions` de ese branch → `CashRegisterId = nuevo_register.Id`.
2. Esto garantiza que datos historicos queden vinculados y que el nuevo unique index no falle.

---

## 6. Cambios por Capa

### 6.1 POS.Domain (Capa de Dominio)

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `Models/CashRegister.cs` | **CREAR** | Nueva entidad con Id, BranchId, Name, DeviceUuid, IsActive, CreatedAt |
| `Models/CashRegisterSession.cs` | **MODIFICAR** | Agregar `CashRegisterId` (int?) + navegacion `CashRegister?` |
| `Models/Branch.cs` | **MODIFICAR** | Agregar navegacion `ICollection<CashRegister>?` |
| `Models/CashRegisterSessionRequest.cs` | **MODIFICAR** | Agregar `CashRegisterId` a `OpenSessionRequest`; agregar nuevos DTOs para CashRegister CRUD |

### 6.2 POS.Repository (Capa de Datos)

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `ApplicationDbContext.cs` | **MODIFICAR** | Agregar `DbSet<CashRegister>`, configuracion de la entidad, modificar unique index de session |
| `IRepository/ICashRegisterRepository.cs` | **CREAR** | Interface con `GetByBranchAsync`, `GetByDeviceUuidAsync` |
| `Repository/CashRegisterRepository.cs` | **CREAR** | Implementacion |
| `IRepository/ICashRegisterSessionRepository.cs` | **MODIFICAR** | Agregar `GetOpenSessionByRegisterAsync(int registerId)` |
| `Repository/CashRegisterSessionRepository.cs` | **MODIFICAR** | Implementar nuevo metodo |
| `IUnitOfWork.cs` | **MODIFICAR** | Agregar `ICashRegisterRepository CashRegisters` |
| `UnitOfWork.cs` | **MODIFICAR** | Registrar nueva propiedad |
| `Migrations/` | **CREAR** | Auto-generada con data seed |

### 6.3 POS.Services (Capa de Servicios)

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `IService/ICashRegisterService.cs` | **MODIFICAR** | Cambiar firmas para aceptar `registerId`; agregar metodos CRUD de CashRegister |
| `Service/CashRegisterService.cs` | **MODIFICAR** | Refactorizar toda la logica de session para operar por register |

### 6.4 POS.API (Capa de Presentacion)

| Archivo | Accion | Detalles |
|---------|--------|----------|
| `Controllers/CashRegisterController.cs` | **MODIFICAR** | Nuevos endpoints CRUD para registers; modificar endpoints de session para aceptar `registerId` |

---

## 7. Refactor Detallado: `CashRegisterService`

### 7.1 Cambios de firma en `ICashRegisterService`

| Metodo actual | Firma nueva | Cambio |
|---------------|-------------|--------|
| `GetOpenSessionAsync(int branchId)` | `GetOpenSessionAsync(int registerId)` | Busca por register en vez de branch |
| `OpenSessionAsync(int branchId, OpenSessionRequest req)` | `OpenSessionAsync(int branchId, int registerId, OpenSessionRequest req)` | Recibe registerId; valida que el register pertenezca al branch y no tenga sesion abierta |
| `CloseSessionAsync(int branchId, CloseSessionRequest req)` | `CloseSessionAsync(int registerId, CloseSessionRequest req)` | Cierra la sesion del register especifico |
| `AddMovementAsync(int branchId, AddMovementRequest req)` | `AddMovementAsync(int registerId, AddMovementRequest req)` | Agrega movimiento a la sesion del register |
| `GetHistoryAsync(int branchId, DateTime from, DateTime to)` | Sin cambios de firma | Sigue retornando historia por branch (todas las cajas) |
| — | **NUEVO** `GetAllRegistersAsync(int branchId)` | Lista todos los registers del branch |
| — | **NUEVO** `CreateRegisterAsync(int branchId, CreateCashRegisterRequest req)` | Crea un register |
| — | **NUEVO** `UpdateRegisterAsync(int registerId, UpdateCashRegisterRequest req)` | Actualiza nombre/DeviceUuid |
| — | **NUEVO** `ToggleRegisterAsync(int registerId)` | Activa/desactiva |
| — | **NUEVO** `GetRegisterByDeviceUuidAsync(int branchId, string deviceUuid)` | Lookup por UUID del dispositivo |

### 7.2 Logica de `OpenSessionAsync` (refactorizada)

```
1. Validar que el CashRegister con registerId exista
2. Validar que register.BranchId == branchId (seguridad)
3. Validar que register.IsActive == true
4. Validar que no exista sesion abierta para ese register
5. Crear CashRegisterSession con BranchId + CashRegisterId
6. Persistir (el unique filtered index protege contra race conditions)
```

### 7.3 Logica de `CloseSessionAsync` (refactorizada)

```
1. Obtener la sesion abierta del register (no del branch)
2. Calcular financials (CashSalesCents ahora debe filtrar por CashRegisterSessionId, no por ventana temporal + branch)
3. Cerrar sesion
```

### 7.4 `CalculateCashSalesAsync` — Refactor necesario

**Actual:** Suma pagos en cash de ordenes del branch en la ventana temporal `OpenedAt → ClosedAt`.

**Problema Multi-Till:** Con multiples cajas abiertas simultaneamente, las ventanas temporales se solapan. El calculo por ventana temporal atribuiria ventas de Caja 1 a Caja 2 si sus horarios se cruzan.

**Solucion:** Usar el FK `Order.CashRegisterSessionId` en vez de la ventana temporal:

```
Actual:  WHERE BranchId = @id AND CreatedAt BETWEEN @opened AND @closed
Nuevo:   WHERE CashRegisterSessionId = @sessionId
```

Este refactor es **obligatorio** para multi-till. No es opcional como se menciona en el design doc original de AUDIT-003.

### 7.5 `GetOpenSessionAsync` en `CashRegisterSessionRepository`

**Nuevo metodo:**
```
GetOpenSessionByRegisterAsync(int registerId):
  WHERE CashRegisterId = @registerId AND Status = 'open'
  Include(Movements)
  FirstOrDefaultAsync
```

**Metodo existente** `GetOpenSessionAsync(int branchId)` se mantiene temporalmente para queries que necesiten "hay alguna sesion abierta en el branch?" (e.g., dashboard). Puede marcarse como `[Obsolete]` o renombrarse a `GetAnyOpenSessionAsync`.

---

## 8. Cambios en Endpoints (Controller)

### 8.1 Nuevos endpoints — CRUD de CashRegister

| Method | Route | Roles | Descripcion |
|--------|-------|-------|-------------|
| GET | `/api/cashregister/registers` | Owner, Manager | Lista todos los registers del branch |
| POST | `/api/cashregister/registers` | Owner, Manager | Crea un nuevo register |
| PUT | `/api/cashregister/registers/{id}` | Owner, Manager | Actualiza nombre/DeviceUuid |
| PATCH | `/api/cashregister/registers/{id}/toggle` | Owner, Manager | Activa/desactiva |
| GET | `/api/cashregister/registers/by-device/{deviceUuid}` | Owner, Manager, Cashier | Lookup por UUID del dispositivo |

### 8.2 Endpoints de sesion modificados

| Endpoint actual | Cambio |
|-----------------|--------|
| `GET /api/cashregister/session` | Agregar `?registerId=N` como query param **requerido** |
| `POST /api/cashregister/session/open` | Agregar `registerId` al body (`OpenSessionRequest`) |
| `POST /api/cashregister/session/close` | Agregar `?registerId=N` como query param |
| `POST /api/cashregister/movement` | Agregar `?registerId=N` como query param |
| `GET /api/cashregister/history` | Sin cambios (retorna todas las sesiones del branch) |

### 8.3 Flujo del frontend

```
1. Al iniciar la app POS, el frontend genera/recupera un DeviceUuid de localStorage
2. GET /api/cashregister/registers/by-device/{uuid}
   - Si encuentra register → lo usa como "su caja"
   - Si no → muestra selector de cajas disponibles o solicita vincular
3. POST /api/cashregister/session/open con { registerId, initialAmountCents, openedBy }
4. Todas las ordenes se sincronizan con el CashRegisterSessionId de la sesion activa
5. POST /api/cashregister/session/close?registerId=N al final del turno
```

---

## 9. Impacto en OrderService

### 9.1 `SyncOrdersAsync` — Sin cambios

Phase 1b ya valida por `CashRegisterSessionId` directo. El frontend envia el sessionId de su register activo. No necesita saber si es multi-till o single-till.

### 9.2 `AddPaymentAsync` — Sin cambios

Valida que `order.CashRegisterSessionId` apunte a una sesion abierta. Funciona independientemente de multi-till.

### 9.3 `SplitOrderAsync`, `MergeOrdersAsync`, `MoveItemsAsync` — Sin cambios

Ya copian/validan `CashRegisterSessionId`. No les importa si la sesion pertenece a un register o no.

---

## 10. Plan de Implementacion Step-by-Step

### Paso 1: Crear entidad `CashRegister` + migration con data seed

**Archivos:**
- `POS.Domain/Models/CashRegister.cs` (CREAR)
- `POS.Domain/Models/CashRegisterSession.cs` (MODIFICAR: +FK)
- `POS.Domain/Models/Branch.cs` (MODIFICAR: +navegacion)
- `POS.Repository/ApplicationDbContext.cs` (MODIFICAR: +DbSet, +config, +cambiar unique index)
- Migration auto-generada con data seed

**Validacion:** `dotnet build` + `dotnet ef migrations add` exitoso.

### Paso 2: Crear repositorio + UnitOfWork

**Archivos:**
- `POS.Repository/IRepository/ICashRegisterRepository.cs` (CREAR)
- `POS.Repository/Repository/CashRegisterRepository.cs` (CREAR)
- `POS.Repository/IRepository/ICashRegisterSessionRepository.cs` (MODIFICAR: +nuevo metodo)
- `POS.Repository/Repository/CashRegisterSessionRepository.cs` (MODIFICAR: +implementacion)
- `POS.Repository/IUnitOfWork.cs` (MODIFICAR: +propiedad)
- `POS.Repository/UnitOfWork.cs` (MODIFICAR: +registrar)

**Validacion:** `dotnet build` exitoso.

### Paso 3: Refactorizar `CashRegisterService` para multi-till

**Archivos:**
- `POS.Domain/Models/CashRegisterSessionRequest.cs` (MODIFICAR: +DTOs nuevos)
- `POS.Services/IService/ICashRegisterService.cs` (MODIFICAR: +firmas nuevas)
- `POS.Services/Service/CashRegisterService.cs` (MODIFICAR: refactor completo de session logic + CRUD de register + refactor `CalculateCashSalesAsync`)

**Validacion:** `dotnet build` exitoso.

### Paso 4: Actualizar controller con nuevos endpoints

**Archivos:**
- `POS.API/Controllers/CashRegisterController.cs` (MODIFICAR: +endpoints CRUD register, +registerId en session endpoints)

**Validacion:** `dotnet build` + test manual en Swagger.

### Paso 5: Aplicar migration a base de datos

```bash
dotnet ef database update --project POS.Repository --startup-project POS.API
```

---

## 11. Resumen de Archivos

| Archivo | Accion | LOC estimado |
|---------|--------|-------------|
| `POS.Domain/Models/CashRegister.cs` | CREAR | ~30 |
| `POS.Domain/Models/CashRegisterSession.cs` | MODIFICAR | +3 |
| `POS.Domain/Models/Branch.cs` | MODIFICAR | +1 |
| `POS.Domain/Models/CashRegisterSessionRequest.cs` | MODIFICAR | +25 |
| `POS.Repository/ApplicationDbContext.cs` | MODIFICAR | +20 |
| `POS.Repository/IRepository/ICashRegisterRepository.cs` | CREAR | ~10 |
| `POS.Repository/Repository/CashRegisterRepository.cs` | CREAR | ~30 |
| `POS.Repository/IRepository/ICashRegisterSessionRepository.cs` | MODIFICAR | +2 |
| `POS.Repository/Repository/CashRegisterSessionRepository.cs` | MODIFICAR | +10 |
| `POS.Repository/IUnitOfWork.cs` | MODIFICAR | +1 |
| `POS.Repository/UnitOfWork.cs` | MODIFICAR | +3 |
| `POS.Services/IService/ICashRegisterService.cs` | MODIFICAR | +20 |
| `POS.Services/Service/CashRegisterService.cs` | MODIFICAR | +80 |
| `POS.API/Controllers/CashRegisterController.cs` | MODIFICAR | +50 |
| `POS.Repository/Migrations/...` | CREAR | auto-generada |

**Total:** 3 archivos nuevos, 11 archivos modificados. ~285 lineas netas.

---

## 12. Lo que NO esta en scope

- Autenticacion de dispositivos por DeviceUuid (requiere middleware de auth).
- Dashboard multi-till en tiempo real (WebSocket/SignalR).
- Transferencia de sesion entre registers.
- Reportes comparativos por caja.
- Limites de cajas por plan de suscripcion.
