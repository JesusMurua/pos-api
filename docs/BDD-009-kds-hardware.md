# BDD-009 — KDS Hardware Integration (Phase 20)

**Fecha:** 2026-04-04
**Estado:** DISEÑO — pendiente de aprobación
**Fase:** 20 — Kitchen Display System

---

## 1. Auditoría KDS — Estado Actual del Backend

### 1.1 ¿Existe código KDS dedicado?

**No.** No existe ningún controlador, servicio ni repositorio con el nombre "KDS" en la base de código actual. La búsqueda de archivos `*Kds*` devuelve cero resultados.

### 1.2 ¿Está soportado el KDS implícitamente?

**Sí, parcialmente.** El `PrintJobController` fue diseñado desde su origen con el KDS en mente. Evidencia directa en el código:

```
// PrintJobController.cs — línea 9-13 (doc comment del controlador)
"Peripheral devices (printers, KDS tablets) poll this controller for pending jobs
and acknowledge completion or failure."

// PrintJobController.cs — líneas 27-30 (doc comment de GetPending)
"Used by printers and KDS tablets to poll for new work."
```

### 1.3 Inventario de artefactos relevantes

| Archivo | Capa | Relevancia KDS |
|---------|------|---------------|
| `POS.API/Controllers/PrintJobController.cs` | API | Expone los 4 endpoints que el KDS ya puede consumir |
| `POS.Domain/Models/PrintJob.cs` | Domain | Modelo de datos completo con `Destination`, `Status`, `RawContent` |
| `POS.Domain/Enums/PrintJobStatus.cs` | Domain | `Pending → Printed \| Failed` |
| `POS.Domain/Enums/PrintingDestination.cs` | Domain | `Kitchen=0, Bar=1, Waiters=2` |
| `POS.Repository/Repository/PrintJobRepository.cs` | Repository | `GetPendingByBranchAsync(branchId, destination)` ya filtra por destino |

### 1.4 Endpoints actuales que el KDS consumirá

| Método | Ruta | Uso KDS |
|--------|------|---------|
| `GET` | `/api/print-jobs/pending?destination=0` | Polling — obtener tickets pendientes de cocina |
| `GET` | `/api/print-jobs/pending?destination=1` | Polling — obtener tickets pendientes de bar |
| `PATCH` | `/api/print-jobs/{id}/printed` | Acknowledge — marcar ticket como procesado |
| `PATCH` | `/api/print-jobs/{id}/failed` | Error — reportar fallo tras intento |

### 1.5 Autenticación existente

El `PrintJobController` requiere `[Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]`.
El rol `Kitchen` ya existe en el sistema. El KDS tablet necesitará un JWT con ese rol y su `branchId` en los claims (extraídos por `BaseApiController`).

### 1.6 Deuda técnica identificada

| # | Deuda | Prioridad |
|---|-------|-----------|
| D1 | El campo `RawContent` almacena texto ESC/POS crudo — el KDS necesita datos estructurados (JSON) para renderizar visualmente los ítems | Alta |
| D2 | No existe un rol/usuario de servicio para el KDS tablet; debe autenticarse con una cuenta `Kitchen` genérica por sucursal | Media |
| D3 | No hay endpoint para que el KDS indique "en preparación" (estado intermedio entre `Pending` y `Printed`) | Media |
| D4 | `MaxAttempts = 3` está hardcodeado en el controlador — para el KDS, el concepto de "intento fallido" es diferente al de una impresora física | Baja |

---

## 2. Integración KDS ↔ PrintJobs

### 2.1 Flujo de vida de un ticket en el KDS

```
[Sync Engine]                [Backend DB]              [KDS Tablet]
     │                            │                         │
     │── POST /orders/sync ──────►│                         │
     │                            │ crea PrintJob           │
     │                            │ Status=Pending          │
     │                            │ Destination=Kitchen     │
     │                            │                         │
     │                            │◄── GET /pending?dest=0 ─│  (polling)
     │                            │──── [PrintJob list] ───►│
     │                            │                         │ muestra ticket
     │                            │                         │ cocinero ve pedido
     │                            │                         │ cocinero presiona ✓
     │                            │◄── PATCH /{id}/printed ─│
     │                            │ Status=Printed          │
     │                            │ PrintedAt=UtcNow        │
     │                            │──── 200 OK ────────────►│
     │                            │                         │ ticket desaparece
```

### 2.2 Estrategia de polling

**Intervalo recomendado:** 5 segundos.

Justificación:
- Un restaurante típico recibe 1-3 órdenes por minuto en hora pico.
- 5s garantiza latencia máxima visible de 5s desde que el cajero confirma hasta que aparece en cocina.
- En reposo genera 12 requests/minuto por dispositivo — carga negligible.

**Cómo filtrar por destino:**

```
KDS Cocina: GET /api/print-jobs/pending?destination=0
KDS Bar:    GET /api/print-jobs/pending?destination=1
```

El `PrintJobRepository.GetPendingByBranchAsync` ya aplica este filtro eficientemente mediante LINQ + EF Core.

### 2.3 Formato de `RawContent` — Deuda D1

El campo `RawContent` fue diseñado para texto ESC/POS crudo (impresoras físicas). El KDS necesita datos estructurados para renderizar colores, agrupar ítems, mostrar modificadores, etc.

**Solución propuesta (Fase 20):** Agregar un campo `StructuredContent` de tipo `nvarchar(max)` a `PrintJob` que contenga un JSON con estructura normalizada. El campo `RawContent` se mantiene para backwards compatibility.

```json
// Estructura propuesta para StructuredContent
{
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
}
```

### 2.4 Estado intermedio "En Preparación" — Deuda D3

Actualmente el ciclo es solo `Pending → Printed | Failed`. Para el KDS es útil tener un estado intermedio que signifique "el cocinero lo vio y está trabajando en él".

**Solución propuesta:** Agregar `PrintJobStatus.InProgress = 3` y un nuevo endpoint:

```
PATCH /api/print-jobs/{id}/in-progress
```

Esto permite al KDS mostrar dos columnas: **Pendientes** | **En Preparación**, similar a sistemas KDS comerciales (Oracle MICROS, TouchBistro KDS).

### 2.5 Autenticación del dispositivo KDS — Deuda D2

**Problema:** Los dispositivos KDS deben autenticarse pero no son usuarios humanos.

**Solución propuesta:** Crear un endpoint de autenticación de dispositivo:

```
POST /api/auth/device-login
Body: { "deviceKey": "...", "branchId": 1, "deviceType": "KDS_KITCHEN" }
Returns: { "token": "JWT con rol Kitchen y branchId" }
```

El `deviceKey` sería un secreto configurado por sucursal en la tabla `Branch`. El JWT tendría expiración larga (30 días) ya que no es una sesión humana.

---

## 3. SignalR / WebSockets — Análisis

### 3.1 Problema que resuelve

Con polling puro, si el cocinero está mirando el KDS, el nuevo ticket tarda hasta 5 segundos en aparecer. SignalR permitiría **push inmediato**: en el momento en que el Sync Engine crea el `PrintJob`, el backend notifica al KDS en tiempo real.

### 3.2 Arquitectura SignalR propuesta

```
[Sync Engine]         [Backend Hub]           [KDS Tablet]
     │                     │                       │
     │── POST /sync ───────►│                       │
     │                     │ crea PrintJob          │
     │                     │                       │
     │                     │─── Hub.SendAsync ─────►│
     │                     │   "NewPrintJob"         │
     │                     │   payload: PrintJob     │
     │                     │                       │ muestra ticket
     │                     │                       │ (< 100ms latencia)
```

**Hub propuesto:** `KdsHub`

```csharp
// Grupos por sucursal+destino — cada KDS se suscribe al suyo
await Groups.AddToGroupAsync(connectionId, $"branch-{branchId}-kitchen");
await Groups.AddToGroupAsync(connectionId, $"branch-{branchId}-bar");

// Evento emitido cuando PrintJob es creado
await _hubContext.Clients
    .Group($"branch-{branchId}-kitchen")
    .SendAsync("NewPrintJob", printJob);
```

### 3.3 Análisis de trade-offs

| Criterio | Polling (actual) | SignalR |
|----------|-----------------|---------|
| Latencia nuevos tickets | 0-5 seg | < 200ms |
| Complejidad backend | Baja | Media (Hub + DI del HubContext) |
| Complejidad cliente KDS | Baja | Media (SignalR JS client) |
| Infraestructura | Solo HTTP | WebSocket / SSE fallback |
| Resiliencia offline | Sin cambios | Requiere lógica de reconexión |
| Azure/cloud scaling | Sin cambios | Requiere Azure SignalR Service en multi-instancia |

### 3.4 Recomendación

**Fase 20: implementar polling (ya funciona) + agregar SignalR como mejora opcional en Fase 20b.**

Razón: El polling con 5 segundos es aceptable para la mayoría de restaurantes. SignalR agrega valor real solo en cocinas de alto volumen (> 20 órdenes/hora). La deuda técnica D1 (datos estructurados) y D3 (estado InProgress) tienen mayor impacto en la experiencia del cocinero que reducir la latencia de 5s a 0.2s.

**Si se decide implementar SignalR**, los únicos archivos nuevos son:
- `POS.API/Hubs/KdsHub.cs`
- Registro en `Program.cs`: `app.MapHub<KdsHub>("/hubs/kds")`
- Inyección de `IHubContext<KdsHub>` en el servicio que crea `PrintJob`s

---

## 4. Plan de Implementación Fase 20

### Alcance mínimo (sin deuda técnica)

El KDS **ya puede funcionar** con los endpoints existentes. No se requiere código nuevo para la funcionalidad básica.

### Alcance completo (eliminando deuda técnica)

| # | Tarea | Archivos afectados |
|---|-------|--------------------|
| 1 | Agregar `StructuredContent` a `PrintJob` | `PrintJob.cs`, migración EF |
| 2 | Poblar `StructuredContent` en el Sync Engine | `OrderSyncService.cs` |
| 3 | Agregar `PrintJobStatus.InProgress` | `PrintJobStatus.cs` |
| 4 | Endpoint `PATCH /{id}/in-progress` | `PrintJobController.cs` |
| 5 | Endpoint `POST /auth/device-login` | `AuthController.cs`, `Branch` model |
| 6 | (Opcional) `KdsHub` + SignalR | `KdsHub.cs`, `Program.cs` |

### Sin código nuevo en esta fase

Esta es una fase de **diseño y documentación**. Ningún archivo de código se modifica en BDD-009 hasta aprobación del diseño.

---

## 5. Preguntas abiertas para el equipo

1. **¿El KDS será un browser (React/Angular) o una app nativa (Android/iOS)?**
   Impacta en si usar SignalR JS client o un SDK nativo.

2. **¿Un KDS tablet puede ver múltiples destinos simultáneamente?**
   (Ej: un restaurante pequeño con un solo monitor para cocina + bar)
   Impacta en la lógica de grupos de SignalR.

3. **¿Se quiere el estado `InProgress` en Fase 20 o es roadmap futuro?**
   Impacta en si agregar la migración EF ahora.

4. **¿El `deviceKey` para autenticación de dispositivo se configura desde el backoffice o hardcoded por sucursal?**
