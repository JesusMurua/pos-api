# Backend Design Document: SignalR KDS Real-Time Push Integration

**Fecha:** 2026-04-04
**Autor:** Arquitecto Senior .NET
**Estado:** DISEÑO — Pendiente de aprobación
**Fase:** 20c — Real-Time Push para Kitchen Display System
**Dependencias:** BDD-008 (Advanced Printing), BDD-009 (KDS Hardware Integration)

---

## 1. Resumen Ejecutivo

Actualmente, los KDS tablets del frontend consumen el endpoint `GET /api/print-jobs/pending?destination={n}` mediante HTTP polling cada 5 segundos para obtener tickets nuevos y en progreso. Aunque este mecanismo es funcional, presenta limitaciones de escalabilidad:

- **Latencia visible:** Un ticket nuevo tarda entre 0 y 5 segundos en aparecer en pantalla.
- **Carga en base de datos:** Cada tablet genera 12 queries/minuto en reposo. Con N dispositivos × M sucursales, la carga crece linealmente sin producir datos nuevos.
- **Experiencia degradada en alto volumen:** Restaurantes con >20 órdenes/hora y múltiples estaciones KDS (cocina, bar, meseros) sufren latencia perceptible y carga acumulada en Supabase/PostgreSQL.

Este diseño propone reemplazar el polling con una conexión WebSocket persistente usando **ASP.NET Core SignalR**, manteniendo el polling como mecanismo de fallback ante desconexiones.

### Beneficios esperados

| Métrica | Polling actual | Con SignalR |
|---------|---------------|-------------|
| Latencia de nuevos tickets | 0–5 seg | < 200ms |
| Queries/minuto por tablet (reposo) | 12 | 0 (push-only) |
| Queries/minuto por tablet (activo) | 12 | Solo en reconexión |
| Carga DB (10 tablets, 3 sucursales) | ~360 queries/min | ~0 en estado estable |

---

## 2. Análisis del Estado Actual

### 2.1 Flujo de vida actual de un PrintJob

```
[POS Frontend]         [OrderService]           [PrintJobController]        [KDS Tablet]
      │                      │                          │                        │
      │── POST /orders/sync ►│                          │                        │
      │                      │ Phase 8:                 │                        │
      │                      │ GeneratePrintJobsAsync() │                        │
      │                      │ crea PrintJob            │                        │
      │                      │ Status=Pending           │                        │
      │                      │ Destination=Kitchen      │                        │
      │                      │                          │                        │
      │                      │                          │◄─ GET /pending?dest=0 ─│ (polling 5s)
      │                      │                          │── [PrintJob list] ────►│
      │                      │                          │                        │ renderiza ticket
      │                      │                          │                        │
      │                      │                          │◄─ PATCH /{id}/in-progress│ (cocinero toca)
      │                      │                          │── 204 ───────────────►│
      │                      │                          │                        │
      │                      │                          │◄─ PATCH /{id}/printed ─│ (orden lista)
      │                      │                          │── 200 + PrintJob ────►│
```

### 2.2 Artefactos existentes relevantes

| Archivo | Capa | Rol en la integración SignalR |
|---------|------|-------------------------------|
| `POS.API/Controllers/PrintJobController.cs` | API | Endpoints de polling y transiciones de estado. El polling (`GetPending`) será reemplazado por push; las mutaciones (`MarkInProgress`, `MarkPrinted`, `MarkFailed`) **se mantienen** y además dispararán eventos SignalR. |
| `POS.Services/Service/PrintJobService.cs` | Services | Lógica de transición `Pending → InProgress`. Punto de inyección para emitir `UpdatePrintJobStatus`. |
| `POS.Services/Service/OrderService.cs` | Services | `GeneratePrintJobsAsync()` (línea ~1291) crea los PrintJobs al sincronizar órdenes. **Punto principal de inyección** para emitir `ReceiveNewPrintJob`. |
| `POS.Domain/Models/PrintJob.cs` | Domain | Modelo con `Id`, `OrderId`, `BranchId`, `Destination`, `Status`, `RawContent`, `StructuredContent`, `CreatedAt`, `PrintedAt`, `AttemptCount`. |
| `POS.Domain/Enums/PrintJobStatus.cs` | Domain | `Pending=0, Printed=1, Failed=2, InProgress=3`. |
| `POS.Domain/Enums/PrintingDestination.cs` | Domain | `Kitchen=0, Bar=1, Waiters=2`. |
| `POS.Repository/Repository/PrintJobRepository.cs` | Repository | `GetPendingByBranchAsync(branchId, destination)` — usado por el polling, se mantiene para fallback. |
| `POS.API/Controllers/BaseApiController.cs` | API | Extrae `BranchId` del claim JWT `"branchId"`. El Hub reutilizará esta misma lógica de extracción. |
| `POS.API/Program.cs` | API | Registro de servicios y middleware. Se agregará el registro de SignalR y el mapping del Hub. |

### 2.3 Autenticación actual

`BaseApiController` extrae `BranchId` del claim JWT `"branchId"` (línea 25-30). Los roles autorizados para PrintJobController son: `Owner, Manager, Cashier, Kitchen, Waiter`. El Hub deberá respetar la misma autenticación JWT y extracción de claims.

### 2.4 Creación de PrintJobs — punto de disparo

En `OrderService.cs`, el método `GeneratePrintJobsAsync()` se ejecuta como **Phase 8** del flujo de sync (línea ~492-501):

```
Phase 8: Generate PrintJobs per destination (best-effort)
→ Agrupa OrderItems por PrintingDestination del Product
→ Crea un PrintJob por grupo con Status=Pending
→ Persiste con _unitOfWork.PrintJobs.AddRangeAsync()
→ SaveChangesAsync()
```

Este es el punto exacto donde se debe emitir `ReceiveNewPrintJob` hacia el grupo SignalR correspondiente.

---

## 3. Requisitos

### 3.1 Requisitos Funcionales

| ID | Requisito | Prioridad |
|----|-----------|-----------|
| RF-01 | Los KDS tablets deben recibir nuevos PrintJobs en tiempo real (< 500ms) sin polling | Alta |
| RF-02 | Los KDS tablets deben recibir actualizaciones de estado de PrintJobs en tiempo real | Alta |
| RF-03 | Cada tablet debe recibir solo los PrintJobs de su `BranchId` | Alta |
| RF-04 | Cada tablet debe poder suscribirse a un `PrintingDestination` específico (Kitchen, Bar, Waiters) | Alta |
| RF-05 | Un tablet debe poder suscribirse a múltiples destinos simultáneamente (restaurante pequeño con un solo monitor) | Media |
| RF-06 | Si la conexión WebSocket se pierde, el tablet debe caer automáticamente a HTTP polling | Alta |
| RF-07 | Al reconectarse por WebSocket, el tablet debe sincronizar el estado completo antes de volver a modo push | Alta |
| RF-08 | Los eventos de cambio de estado generados por otros tablets deben propagarse a todos los suscritos al mismo grupo | Media |

### 3.2 Requisitos No Funcionales

| ID | Requisito | Criterio |
|----|-----------|----------|
| RNF-01 | Latencia de push end-to-end | < 500ms desde `SaveChangesAsync()` hasta renderizado en tablet |
| RNF-02 | Conexiones concurrentes por instancia | Soportar >= 50 conexiones WebSocket simultáneas |
| RNF-03 | Resiliencia ante desconexión | Reconexión automática con backoff exponencial (1s, 2s, 4s, 8s, max 30s) |
| RNF-04 | Sin pérdida de mensajes | El fallback a polling garantiza que ningún PrintJob se pierda durante desconexión |
| RNF-05 | Backward compatibility | El endpoint `GET /api/print-jobs/pending` permanece funcional e inalterado |
| RNF-06 | Overhead en servidor | El Hub no debe ejecutar queries a DB; solo reenvía datos ya disponibles en memoria |

---

## 4. Arquitectura Propuesta

### 4.1 Diagrama de alto nivel

```
[POS Frontend]         [OrderService]         [PrintJobHub]           [KDS Tablet A]
      │                      │                      │                  (Kitchen, Branch 1)
      │── POST /orders/sync ►│                      │                       │
      │                      │ Phase 8:             │                       │
      │                      │ GeneratePrintJobsAsync()                     │
      │                      │ SaveChangesAsync()   │                       │
      │                      │                      │                       │
      │                      │── IHubContext ───────►│                       │
      │                      │  SendAsync(           │                       │
      │                      │   "ReceiveNewPrintJob"│                       │
      │                      │   group: "branch-1-Kitchen")                 │
      │                      │                      │── WebSocket push ────►│
      │                      │                      │                       │ renderiza ticket
      │                      │                      │                       │
                                                                    [KDS Tablet B]
                                                                    (Bar, Branch 1)
                                                                           │
                                                    │  (no recibe — grupo  │
                                                    │   diferente)         │
```

### 4.2 SignalR Hub: `PrintJobHub`

**Ruta:** `POS.API/Hubs/PrintJobHub.cs`
**Endpoint:** `/hubs/print-jobs`

```csharp
/// <summary>
/// SignalR Hub for real-time PrintJob notifications to KDS tablets and printers.
/// Clients join groups based on BranchId and PrintingDestination.
/// </summary>
[Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
public class PrintJobHub : Hub
{
    /// <summary>
    /// Called by the client after connection to subscribe to a specific printing destination.
    /// Adds the connection to the group "branch-{branchId}-{destination}".
    /// BranchId is extracted from the JWT claim — clients cannot spoof it.
    /// </summary>
    /// <param name="destination">The PrintingDestination to subscribe to (Kitchen, Bar, Waiters).</param>
    public async Task SubscribeToDestination(PrintingDestination destination)
    {
        var branchId = GetBranchIdFromClaims();
        var groupName = BuildGroupName(branchId, destination);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Unsubscribe from a specific destination group.
    /// </summary>
    public async Task UnsubscribeFromDestination(PrintingDestination destination)
    {
        var branchId = GetBranchIdFromClaims();
        var groupName = BuildGroupName(branchId, destination);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Builds the canonical group name for a branch + destination pair.
    /// Format: "branch-{branchId}-{destination}" e.g. "branch-1-Kitchen"
    /// </summary>
    public static string BuildGroupName(int branchId, PrintingDestination destination)
        => $"branch-{branchId}-{destination}";

    private int GetBranchIdFromClaims()
    {
        var claim = Context.User?.FindFirst("branchId");
        if (claim == null || !int.TryParse(claim.Value, out var branchId))
            throw new HubException("Missing or invalid branchId claim in token.");
        return branchId;
    }
}
```

### 4.3 Gestión de Conexiones y Grupos

#### Estrategia de agrupamiento

```
Grupo SignalR                    Quién se suscribe
─────────────────────────────    ──────────────────────────────
branch-1-Kitchen                 KDS tablets de cocina, branch 1
branch-1-Bar                     KDS tablets de bar, branch 1
branch-1-Waiters                 Tablets de meseros, branch 1
branch-2-Kitchen                 KDS tablets de cocina, branch 2
...
```

#### Flujo de conexión del cliente

```
1. KDS Tablet se conecta al Hub:
   → const connection = new signalR.HubConnectionBuilder()
       .withUrl("/hubs/print-jobs", { accessTokenFactory: () => jwtToken })
       .withAutomaticReconnect([1000, 2000, 4000, 8000, 16000, 30000])
       .build();

2. Tras conectar, invoca SubscribeToDestination:
   → await connection.invoke("SubscribeToDestination", "Kitchen");

3. (Opcional) Se suscribe a múltiples destinos:
   → await connection.invoke("SubscribeToDestination", "Bar");

4. Recibe eventos push:
   → connection.on("ReceiveNewPrintJob", (printJob) => { ... });
   → connection.on("UpdatePrintJobStatus", (printJob) => { ... });
```

#### Extracción de BranchId

El `BranchId` se extrae **exclusivamente** del JWT `"branchId"` claim (misma lógica que `BaseApiController`). El cliente **no puede** elegir un branch arbitrario — esto garantiza aislamiento multi-tenant.

### 4.4 Eventos SignalR (Server → Client)

| Evento | Payload | Disparado cuando |
|--------|---------|------------------|
| `ReceiveNewPrintJob` | `PrintJob` (completo) | `OrderService.GeneratePrintJobsAsync()` crea un nuevo PrintJob |
| `UpdatePrintJobStatus` | `PrintJob` (completo) | Un PrintJob cambia de estado (`InProgress`, `Printed`, `Failed`) |

#### Payload de ejemplo — `ReceiveNewPrintJob`

```json
{
  "id": 42,
  "orderId": "550e8400-e29b-41d4-a716-446655440000",
  "branchId": 1,
  "destination": "Kitchen",
  "status": "Pending",
  "rawContent": "... ESC/POS text ...",
  "structuredContent": {
    "orderNumber": "A-042",
    "tableLabel": "Mesa 7",
    "items": [
      {
        "quantity": 2,
        "name": "Tacos de Birria",
        "size": "Regular",
        "extras": ["Sin cebolla", "Doble salsa"],
        "notes": "Alergia: gluten"
      }
    ],
    "priority": "normal",
    "createdAt": "2026-04-04T18:30:00Z"
  },
  "createdAt": "2026-04-04T18:30:00.123Z",
  "printedAt": null,
  "attemptCount": 0
}
```

### 4.5 Puntos de Inyección del HubContext

Se inyecta `IHubContext<PrintJobHub>` en los servicios que mutan PrintJobs. El Hub nunca ejecuta queries a DB por sí mismo — solo actúa como relay de datos que ya existen en memoria.

#### 4.5.1 Punto de inyección #1: `OrderService.GeneratePrintJobsAsync()`

**Archivo:** `POS.Services/Service/OrderService.cs`
**Línea aproximada:** ~1348 (después de `AddRangeAsync` + `SaveChangesAsync`)
**Evento:** `ReceiveNewPrintJob`

```csharp
// Pseudocódigo — ubicación del disparo
private async Task GeneratePrintJobsAsync(IEnumerable<Order> orders)
{
    // ... existing logic: group items, create PrintJob objects ...

    if (printJobs.Count == 0) return;

    await _unitOfWork.PrintJobs.AddRangeAsync(printJobs);
    await _unitOfWork.SaveChangesAsync();

    // ── NEW: Push to SignalR groups ──
    foreach (var job in printJobs)
    {
        var groupName = PrintJobHub.BuildGroupName(job.BranchId, job.Destination);
        await _hubContext.Clients.Group(groupName)
            .SendAsync("ReceiveNewPrintJob", job);
    }
}
```

**Consideraciones:**
- El push ocurre **después** de `SaveChangesAsync()` para garantizar que el PrintJob está persistido antes de notificar.
- Si el push falla (ej. no hay clientes conectados), no se lanza excepción — SignalR maneja esto gracefully.
- El bloque está dentro del `try/catch` best-effort existente (línea 497-501).

#### 4.5.2 Punto de inyección #2: `PrintJobService.MarkAsInProgressAsync()`

**Archivo:** `POS.Services/Service/PrintJobService.cs`
**Línea aproximada:** ~37 (después de `SaveChangesAsync`)
**Evento:** `UpdatePrintJobStatus`

```csharp
public async Task<bool> MarkAsInProgressAsync(int id, int branchId)
{
    // ... existing validation and status change ...

    _unitOfWork.PrintJobs.Update(job);
    await _unitOfWork.SaveChangesAsync();

    // ── NEW: Notify all KDS tablets in the same group ──
    var groupName = PrintJobHub.BuildGroupName(job.BranchId, job.Destination);
    await _hubContext.Clients.Group(groupName)
        .SendAsync("UpdatePrintJobStatus", job);

    return true;
}
```

#### 4.5.3 Punto de inyección #3: `PrintJobController.MarkPrinted()`

**Archivo:** `POS.API/Controllers/PrintJobController.cs`
**Línea aproximada:** ~122 (después de `SaveChangesAsync`)
**Evento:** `UpdatePrintJobStatus`

Actualmente la lógica de `MarkPrinted` y `MarkFailed` está inline en el controller. Dos opciones:

| Opción | Descripción | Trade-off |
|--------|-------------|-----------|
| **A: Inyectar HubContext en el Controller** | Agregar `IHubContext<PrintJobHub>` al constructor del controller y emitir después de `SaveChangesAsync()`. | Mínimo refactor, pero viola la separación de responsabilidades (el controller no debería conocer SignalR). |
| **B: Mover lógica a PrintJobService** (recomendada) | Crear `MarkAsPrintedAsync(id, branchId)` y `MarkAsFailedAsync(id, branchId)` en `IPrintJobService`. El servicio maneja la transición Y el push. | Consistente con el patrón de `MarkAsInProgressAsync()`, mantiene el controller thin. |

**Recomendación: Opción B.** Los tres métodos de transición de estado viven en `PrintJobService`, y los tres emiten el evento SignalR correspondiente. El controller queda como un thin wrapper de HTTP.

```csharp
// Nuevos métodos en IPrintJobService
Task<PrintJob?> MarkAsPrintedAsync(int id, int branchId);
Task<PrintJob?> MarkAsFailedAsync(int id, int branchId);
```

### 4.6 Registro en Program.cs

```csharp
// ── Program.cs — Service Registration ──

// After builder.Services.AddControllers()
builder.Services.AddSignalR();

// ── Program.cs — Middleware Pipeline ──

// After app.MapControllers()
app.MapHub<PrintJobHub>("/hubs/print-jobs");
```

#### Configuración CORS para SignalR

La política CORS existente (`AllowFrontend`) debe actualizarse para permitir el handshake de WebSocket:

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["http://localhost:4200", "https://localhost:4200"];

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();  // ← REQUIRED for SignalR WebSocket
    });
});
```

> **Nota importante:** `AllowCredentials()` es mutuamente exclusivo con `AllowAnyOrigin()`. La configuración actual ya usa `WithOrigins(origins)` explícito, por lo que es compatible.

#### Autenticación JWT para WebSocket

SignalR en browsers envía el JWT como query string en la negociación inicial (no como header). Se requiere configurar el evento `OnMessageReceived` en JWT bearer:

```csharp
.AddJwtBearer(options =>
{
    // ... existing TokenValidationParameters ...

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});
```

---

## 5. Estrategia de Fallback y Resiliencia

### 5.1 Principio: SignalR es optimista, Polling es la red de seguridad

El frontend nunca debe depender exclusivamente de SignalR. El diseño es **push-primary, poll-fallback**:

```
┌─────────────────────────────────────────────────────────┐
│                    KDS Tablet Client                     │
│                                                         │
│  ┌──────────────┐    ┌──────────────────────────────┐  │
│  │  SignalR      │    │  HTTP Polling (fallback)      │  │
│  │  Connection   │    │  GET /api/print-jobs/pending   │  │
│  │              │    │                              │  │
│  │  Estado:     │    │  Activo SOLO cuando:         │  │
│  │  Connected ──┼───►│  SignalR.state != Connected   │  │
│  │  Reconnecting│    │                              │  │
│  │  Disconnected│    │  Intervalo: 5 segundos        │  │
│  └──────────────┘    └──────────────────────────────┘  │
│                                                         │
│  Reglas:                                                │
│  1. Si SignalR.state == Connected → polling OFF          │
│  2. Si SignalR.state == Reconnecting → polling ON        │
│  3. Si SignalR reconecta → fetch completo + polling OFF  │
└─────────────────────────────────────────────────────────┘
```

### 5.2 Flujo de desconexión y reconexión

```
Tiempo    Evento                          Acción del cliente
──────    ─────────────────────────────   ──────────────────────────────────
T+0       WebSocket conectado             Modo push. Polling desactivado.
T+30s     Conexión perdida (WiFi drop)    onclose() disparado.
                                          → Activar polling cada 5 segundos.
                                          → withAutomaticReconnect intenta
                                            reconectar: 1s, 2s, 4s, 8s...
T+35s     Polling tick                    GET /pending → renderiza estado actual.
T+37s     SignalR reconecta               onreconnected() disparado.
                                          → GET /pending (sync completo).
                                          → Re-invocar SubscribeToDestination().
                                          → Desactivar polling.
T+37s+    Modo push restaurado            Push events fluyen normalmente.
```

### 5.3 Reconexión y consistencia de estado

Cuando SignalR reconecta, el cliente **debe**:

1. **Re-suscribirse a los grupos** — SignalR no persiste membresía de grupo entre reconexiones. Invocar `SubscribeToDestination()` de nuevo.
2. **Hacer un fetch completo** — `GET /api/print-jobs/pending?destination={n}` para sincronizar cualquier cambio ocurrido durante la desconexión.
3. **Reconciliar estado local** — Reemplazar la lista local con los datos del fetch, no hacer merge, para evitar inconsistencias.

### 5.4 Pérdida de mensajes durante desconexión

SignalR **no garantiza entrega** de mensajes enviados mientras el cliente estaba desconectado. Por diseño:

- Los mensajes enviados a un grupo durante la desconexión de un miembro se **pierden** para ese miembro.
- El fetch completo en reconexión (paso 2 arriba) cubre este gap.
- **No se implementa cola de mensajes server-side** — la complejidad no justifica el beneficio dado que el polling ya cubre el caso.

---

## 6. Modelos de Dominio — Cambios Requeridos

### 6.1 Sin cambios en modelos de dominio

Esta feature **no requiere** nuevos modelos, enums, ni migraciones de base de datos. SignalR opera exclusivamente sobre los modelos existentes:

- `PrintJob` — se envía tal cual como payload de los eventos.
- `PrintJobStatus` — los valores existentes (`Pending`, `InProgress`, `Printed`, `Failed`) cubren todos los estados que se notifican.
- `PrintingDestination` — se usa para construir los nombres de grupo.

### 6.2 Nuevas interfaces de servicio

```csharp
// Métodos nuevos en IPrintJobService (migrar lógica de controller a service)
Task<PrintJob?> MarkAsPrintedAsync(int id, int branchId);
Task<PrintJob?> MarkAsFailedAsync(int id, int branchId);
```

---

## 7. Endpoints — Sin cambios en la API REST

Todos los endpoints existentes en `PrintJobController` permanecen **inalterados y funcionales**:

| Método | Ruta | Estado |
|--------|------|--------|
| `GET` | `/api/print-jobs/pending?destination={n}` | Mantiene — usado como fallback de polling |
| `GET` | `/api/print-jobs/by-order/{orderId}` | Mantiene — sin relación con SignalR |
| `PATCH` | `/api/print-jobs/{id}/in-progress` | Mantiene — además dispara `UpdatePrintJobStatus` vía SignalR |
| `PATCH` | `/api/print-jobs/{id}/printed` | Mantiene — además dispara `UpdatePrintJobStatus` vía SignalR |
| `PATCH` | `/api/print-jobs/{id}/failed` | Mantiene — además dispara `UpdatePrintJobStatus` vía SignalR |

**Nuevo endpoint (SignalR Hub):**

| Protocolo | Ruta | Descripción |
|-----------|------|-------------|
| WebSocket | `/hubs/print-jobs` | Hub de SignalR para push en tiempo real |

---

## 8. Archivos Afectados — Plan de Implementación

### 8.1 Archivos nuevos

| # | Archivo | Descripción |
|---|---------|-------------|
| 1 | `POS.API/Hubs/PrintJobHub.cs` | Hub de SignalR con métodos `SubscribeToDestination`, `UnsubscribeFromDestination` y helper `BuildGroupName` |

### 8.2 Archivos modificados

| # | Archivo | Cambio |
|---|---------|--------|
| 2 | `POS.API/Program.cs` | Agregar `builder.Services.AddSignalR()`, `app.MapHub<PrintJobHub>("/hubs/print-jobs")`, CORS `AllowCredentials()`, JWT `OnMessageReceived` para query string token |
| 3 | `POS.Services/IService/IPrintJobService.cs` | Agregar `MarkAsPrintedAsync(int id, int branchId)` y `MarkAsFailedAsync(int id, int branchId)` |
| 4 | `POS.Services/Service/PrintJobService.cs` | Implementar los dos métodos nuevos + inyectar `IHubContext<PrintJobHub>` + emitir `UpdatePrintJobStatus` en los tres métodos de transición |
| 5 | `POS.Services/Service/OrderService.cs` | Inyectar `IHubContext<PrintJobHub>` + emitir `ReceiveNewPrintJob` después de `SaveChangesAsync()` en `GeneratePrintJobsAsync()` |
| 6 | `POS.API/Controllers/PrintJobController.cs` | Delegar `MarkPrinted` y `MarkFailed` al servicio (reemplazar lógica inline) |
| 7 | `POS.Services/Dependencies/ServiceDependencies.cs` | Sin cambios si ya registra `IPrintJobService` — verificar |

### 8.3 Orden de implementación

```
Paso 1: PrintJobHub.cs (nuevo)
       └─ Hub con SubscribeToDestination, BuildGroupName
       └─ No depende de nada más

Paso 2: Program.cs (modificar)
       └─ AddSignalR(), MapHub, CORS, JWT events
       └─ Depende de: Paso 1

Paso 3: IPrintJobService + PrintJobService (modificar)
       └─ Nuevos métodos + inyección IHubContext + emisión de eventos
       └─ Depende de: Paso 1

Paso 4: PrintJobController (modificar)
       └─ Delegar MarkPrinted/MarkFailed al servicio
       └─ Depende de: Paso 3

Paso 5: OrderService (modificar)
       └─ Inyectar IHubContext + emitir ReceiveNewPrintJob
       └─ Depende de: Paso 1
```

---

## 9. Consideraciones de Seguridad

| # | Consideración | Mitigación |
|---|---------------|------------|
| S-1 | Un tablet malicioso podría intentar suscribirse a un BranchId ajeno | `BranchId` se extrae del JWT server-side. El método `SubscribeToDestination` no acepta `branchId` como parámetro — el cliente no puede elegirlo. |
| S-2 | Token JWT expuesto en query string durante negociación WebSocket | Estándar de la industria para SignalR en browsers. El token viaja sobre TLS (HTTPS). Mitigación adicional: usar tokens de corta vida para conexiones hub. |
| S-3 | Denegación de servicio por muchas conexiones | Configurar `MaximumParallelInvocationsPerClient` y límites de conexión en `AddSignalR()`. Rate limiting existente aplica al handshake HTTP. |
| S-4 | Broadcast de datos sensibles a dispositivos no autorizados | El `[Authorize]` en el Hub valida JWT en cada conexión. Solo roles explícitos pueden conectar. |

---

## 10. Consideraciones de Escalamiento

### 10.1 Single-instance (actual)

La configuración actual corre una sola instancia del API en Render. SignalR in-process funciona sin configuración adicional — los grupos se almacenan en memoria del proceso.

### 10.2 Multi-instance (futuro)

Si se escala a múltiples instancias (load balancer), SignalR requiere un **backplane** para sincronizar grupos entre instancias:

| Opción | Complejidad | Costo |
|--------|-------------|-------|
| Azure SignalR Service | Baja (managed) | ~$50/mes (Standard tier) |
| Redis backplane | Media | Costo del servidor Redis |
| Sticky sessions | Baja | Sin costo adicional, pero limita escalamiento horizontal |

**Recomendación para MVP:** Sticky sessions si se necesita multi-instancia a corto plazo. Azure SignalR Service para producción enterprise.

### 10.3 Configuración recomendada

```csharp
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.MaximumParallelInvocationsPerClient = 1;
});
```

---

## 11. Preguntas Abiertas

| # | Pregunta | Impacto en diseño |
|---|----------|-------------------|
| Q-1 | ¿El KDS tablet será Angular (mismo stack que el POS), React, o app nativa? | Determina si usar `@microsoft/signalr` (JS) o un SDK nativo. Este diseño asume JS client. |
| Q-2 | ¿Se necesita notificar al POS frontend (no solo KDS) cuando un ticket cambia de estado? | Si sí, agregar un grupo adicional `branch-{id}-pos` para que el cajero vea el estado en tiempo real. |
| Q-3 | ¿Cuántos dispositivos KDS se esperan por sucursal en el peor caso? | Impacta el sizing del backplane si se escala a multi-instancia. |
| Q-4 | ¿Se requiere logging/auditoría de conexiones y desconexiones del Hub? | Impacta si se necesita un `OnConnectedAsync`/`OnDisconnectedAsync` override con logging. |
| Q-5 | ¿El deploy actual en Render soporta WebSocket sticky connections? | Si no, evaluar Azure SignalR Service desde el inicio. |

---

## 12. Decisiones Arquitecturales Tomadas

| # | Decisión | Alternativa descartada | Razón |
|---|----------|----------------------|-------|
| AD-1 | Push-primary con poll-fallback (híbrido) | Solo SignalR sin fallback | Resiliencia: WiFi de cocina es inestable; no se puede depender solo de WebSocket |
| AD-2 | Grupos por `branch-{id}-{destination}` | Grupo solo por branch | Granularidad: evita que el KDS de cocina reciba tickets de bar |
| AD-3 | BranchId extraído del JWT server-side | BranchId enviado por el cliente | Seguridad: previene cross-tenant data leaks |
| AD-4 | Mover lógica de transición al servicio | Mantener inline en controller | Consistencia: un solo lugar para estado + push; controller queda thin |
| AD-5 | No implementar cola server-side de mensajes perdidos | Implementar outbox/retry | Simplicidad: el polling de fallback cubre pérdida de mensajes durante desconexión |
| AD-6 | Emitir push después de SaveChangesAsync | Emitir antes de persistir | Consistencia: nunca notificar un PrintJob que no existe en DB |
