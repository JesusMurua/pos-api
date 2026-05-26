# BDD-022 — SignalR Strongly-Typed Hubs & Loopback Test Harness

**Date:** 2026-05-25
**Status:** Draft — pending approval
**Author:** Backend Architecture
**Related:**
- AUDIT-058 §3.5 — Integration Testing gap for SignalR hubs in `POS.IntegrationTests`.
- [BDD-010 — KDS SignalR](BDD-010-kds-signalr.md) — original KDS hub design.
- [BDD-014 — Device Security and Management](BDD-014-device-security-and-management.md) — device-JWT mode that drives bridge group routing.
- [BDD-021 — Dynamic Catalogs API](BDD-021-Dynamic-Catalogs-API.md) — sibling pattern for integration-test extensions.

---

## 1. Executive Summary

**Problem statement.** Both production hubs (`BridgeHub`, `KdsHub`) inherit from the loosely-typed `Hub` base, so every server → client emission is a string literal (`SendAsync("OpenTurnstile", …)`). A typo on the backend silently breaks the local hardware bridge. The integration test project (`POS.IntegrationTests`) has zero SignalR coverage — no `Microsoft.AspNetCore.SignalR.Client` package, no in-process WebSocket harness in `CustomWebApplicationFactory`, and no proof that the six known production events (`SyncAccessData`, `OpenTurnstile`, `AccessAttempted`, `OnWeightUpdated`, `SendEscPosCommand`, `PrintJobCreated`) actually reach connected clients on the right group with the right payload shape.

**Proposed solution.** Convert both hubs to strongly-typed variants (`Hub<IBridgeClient>`, `Hub<IKdsClient>`), update the `BridgeNotifier` adapter and `KdsEventDispatcherWorker` to consume the typed `IHubContext<THub, TClient>`, add the `Microsoft.AspNetCore.SignalR.Client` package to the test project, and extend `CustomWebApplicationFactory` with a `CreateHubConnection<THub>` helper that bridges the SignalR client to the in-process `TestServer.CreateHandler()`. Implement an MVP of three loopback integration tests (one per dispatch pattern); document the remaining three as deferred follow-ups.

**Expected outcome/impact.** Compile-time safety for every outbound hub event (a typo in a method name becomes a build error, not a silent production regression). Repeatable, automated proof that group routing, JWT claims, and payload contracts hold end-to-end. Wire protocol unchanged — the local bridge and KDS terminals in production continue to receive identical JSON frames. Test harness extensible to the three deferred events and any future hub.

---

## 2. Current State Analysis

### 2.1 Existing architecture involved

| Layer | File | Role |
|-------|------|------|
| API — Hubs | [POS.API/Hubs/BridgeHub.cs](../POS.API/Hubs/BridgeHub.cs) | Loose `Hub` base. Inbound: `ProcessScan`, `ReportHealth`, `ProcessWeightRead`. Outbound (hub-internal): `SyncAccessData` (`Clients.Caller`), `OnWeightUpdated` (dashboard group). |
| API — Hubs | [POS.API/Hubs/KdsHub.cs](../POS.API/Hubs/KdsHub.cs) | Loose `Hub` base. No inbound methods. No hub-internal emissions — every KDS event is dispatched by the worker. |
| Services — Adapter abstraction | [POS.Services/IService/IBridgeNotifier.cs](../POS.Services/IService/IBridgeNotifier.cs) | Defines `NotifyAccessGrantedAsync`, `NotifyAccessAttemptAsync`, `SendEscPosCommandAsync`. Lives in `POS.Services` so the service layer never references `POS.API`. |
| API — Adapter implementation | [POS.API/Adapter/BridgeNotifier.cs](../POS.API/Adapter/BridgeNotifier.cs) | Concrete adapter. Injects `IHubContext<BridgeHub>`. Emits `OpenTurnstile`, `AccessAttempted`, `SendEscPosCommand` via `SendAsync("…", …)`. |
| API — Background worker | [POS.API/Workers/KdsEventDispatcherWorker.cs](../POS.API/Workers/KdsEventDispatcherWorker.cs) | Singleton `BackgroundService`. Injects `IHubContext<KdsHub>`. Drains `KdsEventOutbox` (500 ms polling, batch 50) and broadcasts `PrintJobCreated` via `SendAsync("PrintJobCreated", payload, ct)`. Only event with at-least-once delivery. |
| Integration tests | [POS.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs](../POS.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs) | `WebApplicationFactory<Program>` + EF InMemory + `CountingUnitOfWork` (BDD-021 D1 fallback). HTTP-only. No `Server.CreateHandler()` exposure. |
| Test project — csproj | [POS.IntegrationTests/POS.IntegrationTests.csproj](../POS.IntegrationTests/POS.IntegrationTests.csproj) | References `Microsoft.AspNetCore.Mvc.Testing`, `EFCore.InMemory`, `xunit`, `FluentAssertions`, `System.IdentityModel.Tokens.Jwt`. **Does NOT reference `Microsoft.AspNetCore.SignalR.Client`.** |
| Hub registration | [POS.API/Program.cs](../POS.API/Program.cs) | `app.MapHub<KdsHub>("/hubs/kds")` + `app.MapHub<BridgeHub>("/hubs/bridge")`. JWT-via-query-string negotiation configured in `JwtBearerEvents.OnMessageReceived` for paths under `/hubs/`. |

### 2.2 Current pain points

| # | Pain | Source |
|---|------|--------|
| P1 | Method names are string literals (`SendAsync("OpenTurnstile", …)`); a typo silently breaks the local bridge in production without any compile-time warning. | `BridgeNotifier.cs`, `KdsEventDispatcherWorker.cs`, `BridgeHub.cs` hub-internal emissions |
| P2 | The set of events a client is supposed to handle exists only by convention — no contract surface that IDEs/refactor tools can navigate. | All hub-emitting sites |
| P3 | No automated proof that ANY of the six production events reach connected clients. AUDIT-058 §3.5 flagged this explicitly. | `POS.IntegrationTests/` (no SignalR coverage) |
| P4 | `POS.IntegrationTests.csproj` does not reference `Microsoft.AspNetCore.SignalR.Client` — even a smoke test is currently impossible without infrastructure work. | Test csproj |
| P5 | `CustomWebApplicationFactory` does not expose `Server.CreateHandler()`, so even with the client package added there is no bridging path to the in-process test server. | Factory file |
| P6 | When SignalR's typed-hub source-generator graduates, code that uses string-based `SendAsync` will need a second migration pass. Adopting `Hub<TClient>` now puts the codebase on the migration path with no rewrite cost. | Forward-looking |

### 2.3 Performance baseline

- Production wire path is unchanged by this refactor — typed hubs are a compile-time façade; the runtime emits identical JSON Hub Protocol frames. No baseline shift expected for the local bridge or KDS terminals.
- Test path baseline: today there is no SignalR test, so the baseline is "0 ms, 0 coverage". The harness adds per-test connection cost (~50–200 ms for handshake + group join on `TestServer`'s in-process LongPolling transport — see §4.1 D6 note).

---

## 3. Technical Requirements

### 3.1 Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| FR-001 | `BridgeHub` becomes `Hub<IBridgeClient>`. | `class BridgeHub : Hub<IBridgeClient>`. Hub-internal emissions (`PushSyncDataToCaller`, `ProcessWeightRead`) call typed proxy methods, not `SendAsync(string, …)`. |
| FR-002 | `KdsHub` becomes `Hub<IKdsClient>`. | `class KdsHub : Hub<IKdsClient>`. Inbound methods (none today) stay unchanged. |
| FR-003 | `IBridgeClient` enumerates every outbound method emitted by `BridgeHub` and `BridgeNotifier`. | 5 methods: `SyncAccessData`, `OpenTurnstile`, `AccessAttempted`, `OnWeightUpdated`, `SendEscPosCommand`. All return `Task`. |
| FR-004 | `IKdsClient` enumerates every outbound method emitted by `KdsHub` and `KdsEventDispatcherWorker`. | 1 method: `PrintJobCreated`. Returns `Task`. |
| FR-005 | `BridgeNotifier` consumes `IHubContext<BridgeHub, IBridgeClient>`. | Adapter still implements `IBridgeNotifier` (unchanged interface — see D5). Internal calls go `_hub.Clients.Group(…).OpenTurnstile(customerId)`. |
| FR-006 | `KdsEventDispatcherWorker` consumes `IHubContext<KdsHub, IKdsClient>`. | The magic string constant `EventMethodName = "PrintJobCreated"` is removed. Worker invokes `_hubContext.Clients.Group(group).PrintJobCreated(payload, ct)`. |
| FR-007 | `POS.IntegrationTests.csproj` references `Microsoft.AspNetCore.SignalR.Client`. | `dotnet add package Microsoft.AspNetCore.SignalR.Client` at a version aligned with the server (10.0.x). |
| FR-008 | `CustomWebApplicationFactory` exposes a typed helper `CreateHubConnection<THub>(string hubPath, string? jwt)` that wires the SignalR client to the in-process `TestServer`. | The helper returns a connected, ready-to-`StartAsync()` `HubConnection`. `HttpMessageHandlerFactory` is set to `_ => Server.CreateHandler()` per D6. When `jwt` is supplied, `AccessTokenProvider` returns it. |
| FR-009 | Three MVP loopback integration tests are implemented (one per dispatch pattern). | `SyncAccessData_Received_On_Bridge_Connect` (hub-internal), `OpenTurnstile_Received_When_HttpAccess_Granted` (notifier-driven via HTTP), `PrintJobCreated_Received_From_Outbox_Drain` (worker-driven). All three pass green on `dotnet test`. |
| FR-010 | Production wire protocol is unchanged. | The local bridge running today continues to receive identical JSON frames after this refactor. No client-side change required. (See Appendix A.) |
| FR-011 | All authorization, group routing, and feature-gate behavior is preserved. | `[Authorize]` on both hubs, JWT-claim-based group joining in `OnConnectedAsync`, `FeatureGateService` checks on `KdsHub` and `BridgeHub.ProcessScan` all behave identically. |
| FR-012 | The remaining three events are tracked as deferred follow-ups. | `SendEscPosCommand`, `AccessAttempted`, `OnWeightUpdated` listed as bullets in Appendix B "Open questions / out of scope for v1". |

### 3.2 Non-Functional Requirements

| Area | Requirement |
|------|-------------|
| Compile-time safety | A misspelled client method name in any backend dispatch site produces a build error. |
| Runtime cost | Zero. Strong typing is resolved by `DispatchProxy` once per hub at startup; per-emission cost is unchanged from string-based `SendAsync`. |
| Wire compatibility | Frames emitted on the wire are byte-for-byte identical to the current loose-typed implementation (method name + JSON args via SignalR's JSON Hub Protocol). |
| Test runtime | Each loopback test must self-contain (start + dispose its own `HubConnection`) and complete in < 5 s. |
| Test isolation | `xUnit` parallel class execution must not produce cross-class hub-connection interference. Each `IClassFixture<CustomWebApplicationFactory>` gets its own `TestServer` and its own connections. |
| Observability | The factory's `CreateHubConnection<THub>` helper does NOT silently swallow connection failures — exceptions on `StartAsync()` surface to the test. |
| Backwards compatibility | None required — there is no existing typed-hub contract to preserve. The wire protocol invariant (FR-010) covers external consumers. |

---

## 4. Architecture Design

### 4.1 Component Overview

| Component | Responsibility |
|-----------|----------------|
| `IBridgeClient` (new, `POS.API/Hubs`) | Strongly-typed contract for the SignalR client. Single interface listing all 5 outbound methods, regardless of which group ultimately receives each one (see Routing note below). |
| `IKdsClient` (new, `POS.API/Hubs`) | Strongly-typed contract listing the 1 outbound method (`PrintJobCreated`). |
| `BridgeHub : Hub<IBridgeClient>` (refactored) | Inbound methods (`ProcessScan`, `ReportHealth`, `ProcessWeightRead`) **stay as plain instance methods** on the hub class — `Hub<TClient>` only types the OUTBOUND proxy (`Clients.*`), not server methods invoked by clients. Hub-internal emissions (`PushSyncDataToCaller`, `ProcessWeightRead → OnWeightUpdated`) now invoke typed members on `Clients.Caller` / `Clients.Group(…)`. |
| `KdsHub : Hub<IKdsClient>` (refactored) | No inbound methods; no hub-internal emissions today. The type-parameter change is the only modification to this file. |
| `BridgeNotifier` (refactored) | `IBridgeNotifier` interface unchanged (D5). Constructor switches from `IHubContext<BridgeHub>` to `IHubContext<BridgeHub, IBridgeClient>`. Each adapter method replaces its `SendAsync(string, …)` call with a typed proxy invocation. |
| `KdsEventDispatcherWorker` (refactored) | Constructor switches from `IHubContext<KdsHub>` to `IHubContext<KdsHub, IKdsClient>`. The `EventMethodName` constant is deleted. |
| `CustomWebApplicationFactory.CreateHubConnection<THub>` (new helper) | Factory method that builds a `HubConnection` wired to the in-process `TestServer` via `Server.CreateHandler()`. Accepts a hub path (`/hubs/bridge`, `/hubs/kds`) and an optional JWT. Returns a ready-to-`StartAsync()` connection. Disposed by the caller. |
| `CatalogsApiTests` pattern reused | The existing pattern (`IClassFixture<CustomWebApplicationFactory>` + `JwtTestFactory` + per-test `Arrange`) carries over to the new `LoopbackTests` class. |

**Routing note (D3).** A single `IBridgeClient` lists all 5 events even though the hardware group only receives 3 of them (`OpenTurnstile`, `SendEscPosCommand`, `SyncAccessData`) and the dashboard group receives the other 2 (`AccessAttempted`, `OnWeightUpdated`). The typed-hub feature gives **compile-time payload safety** (method signature + parameter types) but does **NOT** enforce runtime routing — that responsibility stays with the hub / adapter code that picks `Clients.Group(BuildHardwareGroupName(…))` vs `BuildDashboardGroupName(…)`. This trade-off keeps the existing single-connection contract for the bridge (one socket on `/hubs/bridge` that may or may not be in hardware mode); splitting into `BridgeHardwareHub` + `BridgeDashboardHub` would force a v2 wire-protocol change on every connected client. Out of scope.

**Inbound vs outbound distinction.** `Hub<TClient>` types only the **server → client** proxy reachable via `Clients.*`. Methods declared directly on the hub class (`public async Task ProcessScan(ScanPayloadDto payload)`) are the **client → server** invocation surface; their typing remains the method's own signature. Bridges invoke them with `connection.InvokeAsync("ProcessScan", payload)` on the client side — that string lookup is unchanged. This refactor is therefore one-sided: outbound typed, inbound unchanged.

**Testing note (D6).** `TestServer.CreateHandler()` returns an `HttpMessageHandler` that routes HTTP requests in-process — there is no actual network socket, so the SignalR client's preferred WebSocket transport is unavailable. The client automatically **downgrades to LongPolling** on connection negotiation. This is expected and fully valid for integration testing: the JSON Hub Protocol frames flowing through LongPolling are byte-identical to those on a WebSocket, and group routing / claim resolution / typed dispatch all exercise the same code paths. Production traffic continues to negotiate WebSockets normally; the transport difference is purely a test-harness artifact.

### 4.2 Data Flow

**Outbound emission (notifier-driven path):**

1. Caller (HTTP controller or service) invokes `IBridgeNotifier.NotifyAccessGrantedAsync(branchId, customerId)`.
2. `BridgeNotifier` resolves `_hub.Clients.Group(BridgeHub.BuildHardwareGroupName(branchId))`. Return type is `IBridgeClient` (instead of `IClientProxy`).
3. Adapter calls `.OpenTurnstile(customerId)` — compile-time bound to `IBridgeClient.OpenTurnstile(int)`.
4. SignalR runtime serializes via JSON Hub Protocol and writes to every connected client in the hardware group. Wire frame is identical to today.

**Outbound emission (worker-driven path):**

1. `KdsEventDispatcherWorker.ExecuteAsync` polls `KdsEventOutbox` every 500 ms (existing behavior).
2. For each pending row, resolves `_hubContext.Clients.Group(KdsHub.BuildGroupName(branchId, destination))` — typed as `IKdsClient`.
3. Calls `.PrintJobCreated(payload, ct)` — typed.
4. On success, marks row `IsProcessed = true`; on failure, leaves it for the next iteration (existing at-least-once semantics unchanged).

**Outbound emission (hub-internal path):**

1. Bridge connects → `BridgeHub.OnConnectedAsync` → `PushSyncDataToCaller()` → `Clients.Caller.SyncAccessData(records)` (typed).
2. Bridge invokes `ProcessWeightRead(payload)` → hub method runs → `Clients.Group(BuildDashboardGroupName(branchId)).OnWeightUpdated(payload)` (typed).

**Loopback test path:**

1. Test resolves `var connection = _factory.CreateHubConnection<BridgeHub>("/hubs/bridge", jwt);` from the factory.
2. Test registers handlers on the connection (e.g. `connection.On<IReadOnlyList<SyncAccessRecordDto>>("SyncAccessData", payload => …);`).
   - **Note:** On the test client side, handlers are still registered by method name string. The typed-hub feature does not provide a client-side typed handler API in the test project — that would require generating a client proxy from `IBridgeClient`, which is outside this BDD's scope. The compile-time guarantee applies to the **server's** dispatch site, not the test's receive site. The test's string is the cross-check that the contract holds end-to-end.
3. Test calls `await connection.StartAsync()`. SignalR negotiates with the in-process `TestServer` via the supplied `HttpMessageHandlerFactory`; transport degrades to LongPolling (see D6 note).
4. JWT travels via `AccessTokenProvider` callback → `JwtBearerEvents.OnMessageReceived` (already configured in `Program.cs` for paths under `/hubs/`) → `Context.User` populated.
5. Test triggers the upstream code path (e.g. HTTP POST to a controller that calls `IBridgeNotifier.NotifyAccessGrantedAsync`).
6. SignalR dispatches typed proxy → wire frame → test client handler fires.
7. Test asserts on captured payload within a bounded `Task.WhenAny(handlerTcs.Task, Task.Delay(timeout))` to avoid hangs.
8. Test disposes the connection in `finally`.

### 4.3 Database schema changes

**None.** This refactor is purely typing and test infrastructure. No new tables, columns, indexes, or seed data.

---

## 5. API Contract

### 5.1 Endpoints

**None added.** No HTTP endpoints change. The existing hub mappings (`app.MapHub<BridgeHub>("/hubs/bridge")` and `app.MapHub<KdsHub>("/hubs/kds")`) are unchanged. Hub method invocation paths from the local bridge and KDS terminals are unchanged.

### 5.2 Service Interface

#### 5.2.1 `IBridgeClient` (new)

| Method | Signature concept | Group target (enforced by hub/adapter) |
|--------|-------------------|----------------------------------------|
| `SyncAccessData` | `Task SyncAccessData(IReadOnlyList<SyncAccessRecordDto> records)` | `Clients.Caller` (bridge-mode device on connect) |
| `OpenTurnstile` | `Task OpenTurnstile(int customerId)` | `bridge-hardware-{branchId}` |
| `SendEscPosCommand` | `Task SendEscPosCommand(EscPosPayloadDto payload)` | `bridge-hardware-{branchId}` |
| `AccessAttempted` | `Task AccessAttempted(AccessResultDto result)` | `bridge-dashboard-{branchId}` |
| `OnWeightUpdated` | `Task OnWeightUpdated(WeightPayloadDto payload)` | `bridge-dashboard-{branchId}` |

All methods MUST return `Task` (SignalR requirement). Payload types are existing DTOs from `POS.Domain.DTOs.Bridge` / `POS.Domain.DTOs.AccessControl` — no new DTOs introduced.

#### 5.2.2 `IKdsClient` (new)

| Method | Signature concept | Group target |
|--------|-------------------|--------------|
| `PrintJobCreated` | `Task PrintJobCreated(string payload, CancellationToken ct = default)` | `branch-{branchId}-{destination}` |

The `payload` parameter type matches the current worker's `evt.Payload` (string carrying the serialized print-job representation). A future refactor MAY tighten this to a typed DTO; out of scope for v1.

#### 5.2.3 `IBridgeNotifier` (unchanged — D5)

The public abstraction in `POS.Services/IService/IBridgeNotifier.cs` retains its three methods (`NotifyAccessGrantedAsync`, `NotifyAccessAttemptAsync`, `SendEscPosCommandAsync`) with identical signatures. The change is internal to the `BridgeNotifier` adapter in `POS.API/Adapter/` — only the `IHubContext<TT>` generic argument switches from `IHubContext<BridgeHub>` to `IHubContext<BridgeHub, IBridgeClient>`. Services consuming the notifier need no modification.

#### 5.2.4 `CustomWebApplicationFactory.CreateHubConnection<THub>` (new helper)

| Aspect | Concept |
|--------|---------|
| Signature | `public HubConnection CreateHubConnection<THub>(string hubPath, string? jwt = null) where THub : Hub` |
| Behavior | Builds a `HubConnectionBuilder` pointed at `Server.BaseAddress + hubPath`. Sets `options.HttpMessageHandlerFactory = _ => Server.CreateHandler()` so the client routes in-process. If `jwt` is supplied, sets `options.AccessTokenProvider = () => Task.FromResult<string?>(jwt)`. Returns the built `HubConnection` in a not-yet-started state (caller invokes `StartAsync` and disposes). |
| Generic constraint | `where THub : Hub` — purely documentation. The hub type itself does not flow into the returned connection (SignalR client does not know about server-side types). |
| Disposal contract | Caller MUST dispose the returned `HubConnection` (`await using var connection = …`). |

---

## 6. Business Logic Specifications

### 6.1 Core Algorithms

**A. Strong-typed client method binding.**
SignalR generates a `DispatchProxy` once per `Hub<TClient>` at host startup. Method calls on `Clients.Caller`, `Clients.Group(…)`, `Clients.User(…)`, etc. are routed through the proxy, which reflects the method name (string-equal to the C# method name) and serializes arguments via the negotiated Hub Protocol. **Net runtime cost is the same as `SendAsync(string, …)`.** The compile-time guarantee is that the method signature exists on `IBridgeClient` / `IKdsClient`; if it does not, the project fails to compile.

**B. Group routing remains imperative.**
The typed proxy does not infer the target group from the method name. The hub / adapter MUST explicitly call `Clients.Group(BridgeHub.BuildHardwareGroupName(branchId))` or `BuildDashboardGroupName(branchId)` before invoking the typed method. The naming convention "method whose target is hardware" vs "method whose target is dashboard" is a documentation responsibility — see the Group target column in §5.2.1.

**C. Test-server in-process transport bridge.**
1. The SignalR client's `HubConnectionBuilder` exposes `options.HttpMessageHandlerFactory` — a `Func<HttpMessageHandler, HttpMessageHandler>` invoked on every outbound HTTP request the client makes.
2. The helper returns `_ => Server.CreateHandler()`, ignoring the default handler the client would have built and routing the request straight into the in-process `TestServer`'s middleware pipeline.
3. The first request is `POST /hubs/{name}/negotiate`. The server responds with available transports. WebSockets are advertised but the in-process handler does not implement the WebSocket upgrade flow — the client falls back to LongPolling automatically.
4. Subsequent traffic is `POST /hubs/{name}` long-polling cycles. JSON Hub Protocol frames are exchanged identically to what a WebSocket would carry.

**D. JWT injection into the hub connection.**
1. The helper sets `options.AccessTokenProvider = () => Task.FromResult<string?>(jwt)` on `HubConnectionBuilder.WithUrl(…, options => …)`.
2. SignalR client appends `?access_token={jwt}` to the negotiate and long-poll URLs.
3. Server's `JwtBearerEvents.OnMessageReceived` (configured in `POS.API/Program.cs`) picks up `access_token` from the query string for paths starting with `/hubs/` and assigns it to `context.Token` — the standard SignalR + JWT auth pattern.
4. `Context.User` is populated by the JWT middleware before `OnConnectedAsync` runs. Existing `branchId` / `businessId` / `mode` claim reads work unchanged.

**E. Loopback assertion pattern.**
For each event-under-test, the test:
1. Creates a `TaskCompletionSource<TPayload>` (`TaskCreationOptions.RunContinuationsAsynchronously`).
2. Registers `connection.On<TPayload>("EventName", payload => tcs.TrySetResult(payload));`.
3. Triggers the upstream code path (HTTP call, direct service invocation, or outbox insert).
4. Awaits `Task.WhenAny(tcs.Task, Task.Delay(timeout))` with a fixed timeout (suggested: 5 s).
5. Asserts on `tcs.Task.IsCompletedSuccessfully` and the resulting payload contents.

### 6.2 Validation Rules

| ID | Rule | Failure mode |
|----|------|--------------|
| VR-001 | Every method on `IBridgeClient` and `IKdsClient` MUST return `Task`. | SignalR runtime throws on registration if the interface contains methods with non-`Task` return types. Project fails fast at host startup. |
| VR-002 | Every payload type used in a client method MUST be JSON-serializable by `System.Text.Json` with the project's default options. | Frames silently drop on the wire if serialization fails — keep payloads to existing DTOs that already round-trip successfully in production. |
| VR-003 | Loopback tests MUST dispose `HubConnection` in `finally` (or use `await using`). | Skipping disposal leaks server-side connections, leading to `TestServer` resource exhaustion across the test class. |
| VR-004 | Loopback tests MUST bound their waits with a timeout. | Without a timeout, a hub-side regression (event never fires) hangs the test runner instead of producing a clear failure. |
| VR-005 | The `JwtTestFactory.CreateDeviceToken` overload MUST be used for bridge-mode tests; the `CreateUserToken` overload MUST be used for dashboard / KDS-user tests. | Wrong-mode tokens hit the wrong group (or get aborted in `OnConnectedAsync` by feature-gate / `EnsureBridgeMode` checks) and the test fails with an opaque "no event received" instead of an auth diagnostic. |

### 6.3 Edge Cases

| # | Scenario | Expected behavior |
|---|----------|--------------------|
| EC-1 | Local bridge in production connects after this refactor ships. | Identical experience — wire frames unchanged (FR-010). No client-side update required. |
| EC-2 | Test client cannot negotiate WebSocket because `TestServer` does not implement the upgrade. | Client falls back to LongPolling automatically. Test passes. Log line `Information: Transport selected: LongPolling` is expected. |
| EC-3 | Two test classes run in parallel (xUnit default), each with its own `IClassFixture<CustomWebApplicationFactory>`. | Each factory has its own `TestServer` instance and its own in-process pipeline. Connections from class A's tests do not reach class B's hub instances. No interference. |
| EC-4 | `BridgeHub.OnConnectedAsync` aborts the connection because the JWT lacks `branchId` or `businessId`. | Test client's `StartAsync()` resolves successfully (negotiation succeeded) but the connection terminates immediately. Tests that rely on a successful connect MUST assert connection state before triggering the upstream event. |
| EC-5 | `KdsHub.OnConnectedAsync` aborts because the business lacks `RealtimeKds`. | Same as EC-4 — `StartAsync` succeeds, connection drops. Tests for KDS events MUST seed the feature gate (or use a tenant that has it enabled by default per seed). |
| EC-6 | A typed method is invoked on a group that currently has no connections. | SignalR no-ops silently (existing behavior). The loopback test will time out per VR-004 and fail with a clear "event not received" assertion. |
| EC-7 | A non-MVP event (e.g. `SendEscPosCommand`) is invoked from production code but no test covers it. | Wire behavior is unchanged. Compile-time safety on the typed method name still applies (typo = build failure). Test coverage gap is tracked in Appendix B. |
| EC-8 | The `OnWeightUpdated` echo from `BridgeHub.ProcessWeightRead` exposes a two-way data flow: bridge → hub method → dashboard group. Strong typing makes this implicit flow more visible. | Documented in Appendix B as a candidate for future architectural review; no behavior change in v1. |

---

## 7. Performance Optimization Strategy

### 7.1 Query Optimization

**N/A.** No new queries introduced. Existing repository calls in `BridgeHub.OnConnectedAsync → PushSyncDataToCaller` (eager-loaded customer + active membership query) are unchanged.

### 7.2 Bulk Operations

**N/A.** No batch processing introduced. The `KdsEventDispatcherWorker` continues its existing 50-row batch drain at 500 ms polling.

### 7.3 Caching Strategy

**N/A for this refactor.** No new caches. The existing `CatalogService` cache (BDD-021) is unrelated. The KDS outbox provides existing at-least-once delivery without an in-memory cache layer.

---

## 8. Error Handling Strategy

| Scenario | Exception / status | Body / behavior |
|----------|-------------------|-----------------|
| Inbound hub method (`ProcessScan`, etc.) throws inside its body. | `HubException` propagates to the calling client. | Unchanged from current behavior. |
| Typed proxy method invoked but the target group is empty. | No exception. | Silent no-op (standard SignalR). Loopback tests use VR-004 timeout to surface as a test failure. |
| Test client fails to negotiate (`StartAsync` throws). | `HubException` / `HttpRequestException` surfaces to the test. | Helper does NOT catch; test must `try/catch` or let xUnit report. |
| `Server.CreateHandler()` returns a handler that throws on a particular request (e.g. unauthorized). | Standard ASP.NET pipeline behavior — request returns 401/403. SignalR client surfaces it as a connection failure. | Test should call with a valid JWT (VR-005) or expect the failure explicitly. |
| `KdsEventDispatcherWorker` typed call throws (transport-level failure). | Existing `try/catch` in the worker leaves `IsProcessed = false`; next iteration retries. | Unchanged from current loose-typed behavior. |

**Logging requirements.** No new log lines added by this refactor. The existing `BridgeHub` and `KdsEventDispatcherWorker` logging paths are preserved.

---

## 9. Testing Requirements

### 9.1 Unit test scenarios

**Status: Deferred to future BDD — see Appendix B.** The integration suite (§9.2) covers the load-bearing behaviors end-to-end. A dedicated unit-test track would add value if `POS.UnitTests` is introduced, but is not required to validate this refactor's correctness.

### 9.2 Integration test scenarios (`POS.IntegrationTests/Hubs/LoopbackTests.cs`)

| # | ID | Test method | Pattern | MVP? |
|---|----|-------------|---------|------|
| 1 | IT-LB-1 | `SyncAccessData_Received_On_Bridge_Connect` | Hub-internal emission triggered by `OnConnectedAsync` for bridge-mode device. Seed at least one active membership for the tenant; assert handler receives a non-empty `IReadOnlyList<SyncAccessRecordDto>` within timeout. | **MVP** |
| 2 | IT-LB-2 | `OpenTurnstile_Received_When_HttpAccess_Granted` | Notifier-driven: bridge-mode device subscribes to hardware group; test sends authenticated HTTP request that exercises a granted-access code path (or invokes `AccessControlService.EvaluateScanAsync` via direct service resolution); assert `OpenTurnstile` event with correct `customerId`. | **MVP** |
| 3 | IT-LB-3 | `PrintJobCreated_Received_From_Outbox_Drain` | Worker-driven: KDS-mode device subscribes to `branch-{id}-{destination}` group; test inserts a `KdsEventOutbox` row directly via `IUnitOfWork`; wait up to (polling interval + grace) for `PrintJobCreated` handler to fire; assert payload. | **MVP** |
| 4 | IT-LB-4 | `SendEscPosCommand_Received_When_HttpPrint_Invoked` | Notifier-driven via `HardwareController.Print`. | Deferred (Appendix B) |
| 5 | IT-LB-5 | `AccessAttempted_Received_On_Dashboard_Group` | Notifier-driven; dashboard-group subscription. | Deferred (Appendix B) |
| 6 | IT-LB-6 | `OnWeightUpdated_Received_When_Bridge_Reports_Weight` | Hub-method echo: bridge invokes `ProcessWeightRead` via the client connection's `InvokeAsync`; dashboard-group subscriber receives. | Deferred (Appendix B) |

**Common requirements across all six tests:**

- `IClassFixture<CustomWebApplicationFactory>` shared (each gets its own factory, own server, own connections).
- Use `await using var connection = _factory.CreateHubConnection<THub>(path, jwt);`.
- Register handler before `StartAsync()`.
- Use a `TaskCompletionSource<TPayload>` + `Task.WhenAny(tcs.Task, Task.Delay(5_000))` timeout pattern.
- Assert `tcs.Task.IsCompletedSuccessfully` AND payload contents.
- Dispose the connection via `await using` or `finally`.

### 9.3 Performance test criteria

**Status: Deferred to future BDD — see Appendix B.** Hub-throughput and connection-density load tests would require k6 / NBomber tooling not currently in CI. The MVP integration tests already validate per-event end-to-end latency at single-connection scale.

---

## 10. Implementation Phases

**Scope.** This BDD covers (a) the type-safety refactor of `BridgeHub` and `KdsHub`, (b) the adapter / worker updates that consume the typed `IHubContext<THub, TClient>`, and (c) the addition of a loopback test harness to `POS.IntegrationTests` plus three MVP tests. The remaining three integration tests (IT-LB-4, IT-LB-5, IT-LB-6) are documented but deferred. No production wire-protocol changes; no database schema changes; no new HTTP endpoints.

| Phase | Deliverable | Depends on | Complexity |
|-------|-------------|------------|------------|
| **P1 — Client interfaces** | Define `IBridgeClient.cs` and `IKdsClient.cs` in `POS.API/Hubs/`. Each lists every outbound method per §5.2 with correct payload types and `Task` return. XML docs explain that hub typing enforces payload safety, not group routing. | — | Low |
| **P2 — BridgeHub refactor** | Change `class BridgeHub : Hub` → `class BridgeHub : Hub<IBridgeClient>`. Refactor hub-internal emissions (`PushSyncDataToCaller`, `ProcessWeightRead`) to invoke typed methods. Inbound methods (`ProcessScan`, `ReportHealth`, `ProcessWeightRead`) unchanged. | P1 | Low |
| **P3 — KdsHub refactor** | Change `class KdsHub : Hub` → `class KdsHub : Hub<IKdsClient>`. No hub-internal emissions to migrate. | P1 | Low |
| **P4 — Adapter + worker refactor** | `BridgeNotifier`: switch `IHubContext<BridgeHub>` → `IHubContext<BridgeHub, IBridgeClient>`; replace each `SendAsync("…", …)` with typed proxy call. `KdsEventDispatcherWorker`: switch `IHubContext<KdsHub>` → `IHubContext<KdsHub, IKdsClient>`; delete `EventMethodName` constant; invoke typed `PrintJobCreated`. | P2, P3 | Low |
| **P5 — Test infrastructure** | Add `Microsoft.AspNetCore.SignalR.Client` to `POS.IntegrationTests.csproj` at a version aligned with the server. Extend `CustomWebApplicationFactory` with the `CreateHubConnection<THub>` helper per §5.2.4 (wiring `Server.CreateHandler()` via `HttpMessageHandlerFactory`; optional JWT via `AccessTokenProvider`). | — | Low |
| **P6 — MVP loopback tests** | Implement IT-LB-1, IT-LB-2, IT-LB-3 in `POS.IntegrationTests/Hubs/LoopbackTests.cs`. Use the assertion pattern from §6.1.E. Group by `#region` (Hub-Internal / Notifier-Driven / Worker-Driven). | P4, P5 | Medium |
| **P7 — Build + test** | `dotnet build pos-api.slnx` (halt on error). `dotnet test POS.IntegrationTests/POS.IntegrationTests.csproj --logger "console;verbosity=detailed"` (halt on any failure; report diagnostic). Capture per-test durations for the Pass/Fail matrix. | P6 | Low |
| **P8 — Documentation** | Update CLAUDE.md if a new section about SignalR test infrastructure is warranted (proposal: add a bullet under "Testing" pointing at the helper). Update §9.2 of this BDD with the actual Pass/Fail matrix from P7. Mark P1–P8 as COMPLETED in §10 status row. | P7 | Low |

**Critical path.** P1 → (P2 ∥ P3) → P4. P5 runs in parallel with P1–P4. P6 depends on P4 + P5. P7 depends on P6. P8 depends on P7.

**Estimated total complexity.** Medium. Dominated by P6 (loopback test authoring, including correct JWT mode selection per test and timeout discipline). Hub/adapter/worker refactors (P1–P4) are mechanical low-risk changes once `IBridgeClient` / `IKdsClient` are defined.

---

## Appendix A — Wire protocol invariance

The local hardware bridge (Windows service) and KDS terminals connected to production today continue to receive **byte-identical JSON Hub Protocol frames** after this refactor. Reasoning:

- The SignalR runtime's wire emission goes through the same `HubProtocol.WriteMessage` path regardless of whether the server-side dispatch was string-based (`SendAsync("Method", args)`) or typed-proxy-based (`Clients.Group(…).Method(args)`).
- The typed proxy reflects the C# method name from `IBridgeClient` / `IKdsClient` and uses it as the frame's `target` field. The method names in the new interfaces match the existing literals exactly (`OpenTurnstile`, `SendEscPosCommand`, `SyncAccessData`, `AccessAttempted`, `OnWeightUpdated`, `PrintJobCreated`).
- Argument serialization uses the same `JsonHubProtocol` / `MessagePackHubProtocol` registered in the `AddSignalR(…)` configuration. Payload types are unchanged (existing DTOs from `POS.Domain.DTOs`).

No coordinated client-side release is required. The local bridge in production can be updated on its own cadence with no API-side dependency.

---

## Appendix B — Open questions / out of scope for v1

- **Deferred integration tests (IT-LB-4, IT-LB-5, IT-LB-6).** `SendEscPosCommand`, `AccessAttempted`, and `OnWeightUpdated` are documented in §9.2 but not implemented in this BDD's MVP. Each follows one of the three already-validated patterns and can be added incrementally without new harness work. Owner: backend test track, no fixed date.
- **Unit Tests (UT level).** Deferred consistent with BDD-021 precedent — a `POS.UnitTests` project does not exist yet. Hub typing does not introduce business logic that would benefit from unit coverage beyond what the integration suite validates.
- **Performance Tests (PT level).** Hub-throughput and connection-density load testing requires k6 / NBomber tooling not present in CI. Deferred until a dedicated performance track is opened.
- **`BridgeHub.ProcessWeightRead` implicit data flow.** Bridge invokes `ProcessWeightRead` → hub emits `OnWeightUpdated` to dashboard group. Strong typing exposes this implicit two-way pattern more clearly. Candidate for future architectural review (split into separate inbound + outbound handlers, or move the echo logic into an adapter). No behavior change in v1.
- **Hub-splitting (`BridgeHardwareHub` + `BridgeDashboardHub`).** Would let `Hub<TClient>` enforce hardware-vs-dashboard routing at compile time, but requires a v2 wire-protocol change for every connected client (new hub path). Out of scope; current single-hub-with-groups design retained.
- **SignalR typed-hub source generator.** When/if Microsoft graduates a source generator that generates client-proxy types from `IBridgeClient` for consumption by client projects, the test side could also become strongly typed (instead of `connection.On<TPayload>("EventName", …)` strings). Not yet GA. Revisit when stable.
- **Hub method versioning / negotiation.** No mechanism today for the bridge to declare which method-name vocabulary it understands. Acceptable while we control both endpoints; revisit if third-party clients integrate.
