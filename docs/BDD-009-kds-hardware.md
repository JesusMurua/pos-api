# BDD-009 — KDS Hardware Integration (Phase 20)

> **Status:** Design — pending implementation  
> **Phase:** 20  
> **Related files:** `PrintJobController.cs`, `PrintJob.cs`, `PrintJobStatus.cs`, `PrintingDestination.cs`

---

## 1. KDS Audit — Current State

### 1.1 What exists

There is **no dedicated KDS controller**. Kitchen Display System support is handled entirely through the generic `PrintJobController` (`/api/print-jobs`), which was designed from the start to serve both physical printers and KDS tablets.

#### Relevant endpoints (Phase 19)

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/api/print-jobs/pending?destination=0` | KDS polls for pending kitchen tickets (FIFO) |
| `GET` | `/api/print-jobs/by-order/{orderId}` | POS front-end reads print status per order |
| `PATCH` | `/api/print-jobs/{id}/printed` | KDS acknowledges a completed ticket |
| `PATCH` | `/api/print-jobs/{id}/failed` | KDS records a failed display attempt |

#### PrintJob model

```csharp
public class PrintJob
{
    public int Id { get; set; }
    public string OrderId { get; set; }       // GUID (36 chars)
    public int BranchId { get; set; }
    public PrintingDestination Destination { get; set; }
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
    public string RawContent { get; set; }    // Pre-rendered ESC/POS text
    public DateTime CreatedAt { get; set; }
    public DateTime? PrintedAt { get; set; }
    public int AttemptCount { get; set; }     // Max 3 before → Failed
}
```

#### Enums

```csharp
public enum PrintJobStatus  { Pending = 0, Printed = 1, Failed = 2 }
public enum PrintingDestination { Kitchen = 0, Bar = 1, Waiters = 2 }
```

#### Database indices (Migration `AddPrintingDestinationAndPrintJobs`)

| Index | Columns | Purpose |
|-------|---------|---------|
| `IX_PrintJobs_BranchId_Status` | `BranchId`, `Status` | Fast polling query |
| `IX_PrintJobs_OrderId` | `OrderId` | Fast order-scoped lookup |

### 1.2 Related domain concept — `Order.KitchenStatus`

The `Order` entity has a **separate** `KitchenStatus` field (enum: `Pending`, `Ready`, `Delivered`).  
This field tracks the **order-level** lifecycle visible to waiters and managers.  
It is **not** automatically synchronized with `PrintJob.Status`; that bridge must be implemented in Phase 20.

### 1.3 Gaps identified

| Gap | Impact |
|-----|--------|
| No dedicated KDS role/scope in JWT | KDS must authenticate as `Kitchen` role — acceptable for MVP |
| `Order.KitchenStatus` not updated when all KDS tickets are marked `Printed` | Waiters cannot know when a table's food is ready |
| `RawContent` stores ESC/POS text, not structured JSON | KDS must parse ticket text or ignore it and use order metadata |
| No real-time push from backend to KDS | KDS must poll — introduces latency and unnecessary requests |
| No retry visibility | Restaurant manager cannot see which jobs are stuck in `Failed` state |

---

## 2. KDS ↔ PrintJobs Integration Design

### 2.1 Polling flow (MVP — no SignalR)

```
KDS tablet (Kitchen)
│
│  every 5–10 s
├─► GET /api/print-jobs/pending?destination=0
│       ◄── [ PrintJob[], ordered by CreatedAt ASC ]
│
│  For each job received:
│   • Display ticket on KDS screen
│   • Chef marks order as ready (tap)
├─► PATCH /api/print-jobs/{id}/printed
│       ◄── 200 OK  { job with Status = Printed, PrintedAt = UTC }
│
│  On display/connection error:
├─► PATCH /api/print-jobs/{id}/failed
│       ◄── 200 OK  { job with AttemptCount++ }
│               If AttemptCount ≥ 3 → Status = Failed (no retry)
```

### 2.2 KitchenStatus synchronization

When the KDS marks a `PrintJob` as `Printed`, the backend should check whether **all** `PrintJob` records for the same `OrderId` are in a terminal state (`Printed` or `Failed`). If so, `Order.KitchenStatus` should be transitioned to `Ready`.

**Proposed change to `PrintJobController.MarkPrinted`:**

```csharp
// After saving the Printed status, check if all jobs for the order are done
var allJobs = await _unitOfWork.PrintJobs.GetByOrderAsync(job.OrderId);
bool allDone = allJobs.All(j => j.Status != PrintJobStatus.Pending);

if (allDone)
{
    var order = await _unitOfWork.Orders.GetByIdAsync(job.OrderId);
    if (order != null && order.KitchenStatus == KitchenStatus.Pending)
    {
        order.KitchenStatus = KitchenStatus.Ready;
        order.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
    }
}
```

> **Note:** This logic should be extracted to `PrintJobService` following the layered architecture standards, keeping the controller thin.

### 2.3 New endpoint — `PATCH /api/orders/{orderId}/kitchen-status`

To allow the KDS (or a waiter) to manually advance the kitchen status (e.g., `Ready` → `Delivered`):

| Field | Value |
|-------|-------|
| Method | `PATCH` |
| Route | `/api/orders/{orderId}/kitchen-status` |
| Body | `{ "status": "Delivered" }` |
| Auth | `Kitchen`, `Waiter`, `Manager`, `Owner` |
| Returns | Updated `Order` |

This endpoint decouples the manual waiter acknowledgment from the automatic KDS completion flow.

### 2.4 KDS authentication

The existing `Kitchen` role covers the KDS tablet JWT claims. No new role is needed.  
The KDS device should authenticate via the standard `POST /api/auth/login` using a dedicated kitchen user account, receiving a JWT with `role = Kitchen` and the correct `branchId` claim.

### 2.5 Structured ticket content (future consideration)

`PrintJob.RawContent` currently stores pre-rendered ESC/POS plain text. A KDS screen is not a printer — it benefits from structured data (item names, quantities, modifiers, table number) rather than raw printer markup.

**Recommended approach for Phase 20:**

- Keep `RawContent` for physical printers (backward compatible).
- Add an optional `StructuredContent` (`nvarchar(max)`, JSON) column to `PrintJob` for KDS-friendly payloads.
- The Sync Engine populates `StructuredContent` when `Destination = Kitchen`.

```json
{
  "orderNumber": 42,
  "tableName": "Mesa 5",
  "items": [
    { "name": "Tacos al pastor", "quantity": 2, "notes": "Sin cebolla" },
    { "name": "Quesadilla", "quantity": 1, "notes": "" }
  ],
  "createdAt": "2026-04-04T06:52:23Z"
}
```

> This is optional for the MVP; the KDS can parse order data from `GET /api/orders/{id}` using the `OrderId` from the `PrintJob`.

---

## 3. SignalR / WebSockets Analysis

### 3.1 Current situation

There is **no SignalR infrastructure** in the project. The existing push notification system (`PushController`) uses the Web Push API (VAPID), which is browser-targeted and unsuitable for persistent KDS-to-backend channels.

### 3.2 Problem with polling

With the MVP polling approach (every 5–10 s):

- **Latency:** Up to 10 seconds between an order arriving and the KDS displaying it.
- **Unnecessary requests:** Most polls return an empty list.
- **Scale:** Each KDS tablet = 1 active polling connection. Low overhead at restaurant scale (typically 1–3 tablets per branch).

### 3.3 SignalR value assessment

| Criteria | Polling | SignalR |
|----------|---------|---------|
| Implementation cost | Low (already done) | Medium (new Hub + client adapter) |
| Latency | 5–10 s | < 1 s |
| Server load per tablet | 1 req / 5–10 s | Persistent connection (keep-alive) |
| Complexity | Simple | Requires Hub, group management, reconnect logic |
| Offline resilience | Tablet reconnects and polls missed jobs | Must re-subscribe and handle missed events |
| Viable for restaurant scale | ✅ Yes | ✅ Yes |

### 3.4 Recommended approach

**Phase 20 MVP:** Implement polling only.  
The latency is acceptable for kitchen operations at restaurant scale, and polling is self-healing — a KDS that goes offline and reconnects will automatically pick up missed jobs without any special event replay logic.

**Phase 21 (optional upgrade):** Add SignalR if any of the following conditions are met:

1. Restaurant operates > 3 KDS tablets simultaneously and polling load becomes measurable.
2. Real-time < 1 s latency is a product requirement (e.g., high-volume fast food).
3. The frontend also needs real-time order updates (POS dashboard) — in that case, a shared SignalR hub would serve both use cases.

### 3.5 SignalR design sketch (for Phase 21 reference)

```
Backend                          KDS tablet
   │                                │
   │  PrintJob created              │
   ├──► Hub.SendAsync(              │
   │       "kitchen-{branchId}",    │
   │       "NewPrintJob", job)  ────►│ onNewPrintJob(job)
   │                                │  → display ticket immediately
   │                                │
   │                                │  chef taps "Done"
   │  ◄──── PATCH /printed ─────────┤
   │  job.Status = Printed          │
   │  check all jobs done           │
   │  order.KitchenStatus = Ready   │
   │  Hub.SendAsync(                │
   │       "branch-{branchId}",     │
   │       "OrderReady", orderId) ──►│ POS front-end notified
```

**Hub registration sketch:**

```csharp
// Program.cs
builder.Services.AddSignalR();
app.MapHub<KdsHub>("/hubs/kds");

// KdsHub.cs
public class KdsHub : Hub
{
    public async Task JoinBranch(int branchId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"kitchen-{branchId}");
}
```

**Notification injection point:**

```csharp
// PrintJobService or PrintJobController, after creating PrintJob
await _hubContext.Clients
    .Group($"kitchen-{job.BranchId}")
    .SendAsync("NewPrintJob", job);
```

> **Decision:** Do not implement SignalR in Phase 20. Re-evaluate in Phase 21 after observing real-world polling metrics.

---

## 4. Phase 20 Implementation Checklist

- [ ] **4.1** Extract KDS/kitchen logic from `PrintJobController` into a `PrintJobService` (layered architecture).
- [ ] **4.2** Auto-advance `Order.KitchenStatus` to `Ready` when all `PrintJob`s for an order reach a terminal state (`Printed` or `Failed`).
- [ ] **4.3** Add `PATCH /api/orders/{orderId}/kitchen-status` endpoint for manual waiter/KDS override.
- [ ] **4.4** (Optional) Add `StructuredContent` JSON column to `PrintJob` for KDS-friendly payloads.
- [ ] **4.5** Document KDS polling interval recommendation in the frontend integration guide.
- [ ] **4.6** Add `Failed` job visibility endpoint or dashboard so managers can retry/dismiss stuck jobs.
- [ ] **4.7** Evaluate SignalR after Phase 20 delivery → promote to Phase 21 if needed.

---

## 5. Technical Debt Register

| Item | Severity | Notes |
|------|----------|-------|
| `Order.KitchenStatus` not auto-updated by PrintJob lifecycle | High | Blocks waiter ready-notification feature |
| No structured KDS payload (`RawContent` is ESC/POS text) | Medium | KDS must call `GET /api/orders/{id}` for display metadata |
| No manager view for `PrintJobStatus.Failed` jobs | Medium | Silent failures; food may never reach kitchen |
| No polling interval enforcement | Low | Client-side concern; document in integration guide |
| SignalR not implemented | Low | Acceptable for MVP; revisit in Phase 21 |
