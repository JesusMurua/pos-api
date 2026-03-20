# CLAUDE.md — pos-api

This file provides guidance to Claude Code when working with this repository.
Always read the `.claude/` folder standards before implementing anything.

---

## Project Overview

**POS API** — Backend REST API for the POS Táctil system.
Serves the Angular 18 frontend (restaurant-app) with product catalog,
order management, business configuration, and offline sync support.

**Core philosophy:** Clean architecture, repository pattern, high-performance APIs.
Built to scale from a single food truck to multiple locations.

---

## Standards & Guidelines

All coding decisions must follow these documents in order of precedence:

| File | Purpose |
|------|---------|
| `.claude/response-guidelines.md` | How to analyze, confirm, and implement |
| `.claude/dotnet-api-standards.md` | .NET API architecture, controllers, services, repositories |

> **Language rule:** All code, variables, methods, comments, and XML docs in **English**.
> Explanations and chat responses in **Spanish**.

---

## Tech Stack

- **.NET 10** — ASP.NET Core Web API
- **Entity Framework Core** — ORM, Code First migrations
- **SQL Server / Azure SQL** — Database
- **JWT** — Authentication (planned)
- **Swagger/OpenAPI** — API documentation
- **Serilog** — Logging

---

## Project Structure

```
pos-api/
├── .claude/
├── POS.API/          ← Controllers, Middleware, Program.cs
├── POS.Domain/       ← Models, Enums, Exceptions, Interfaces
├── POS.Repository/   ← EF Core, DbContext, Repositories, UnitOfWork
├── POS.Services/     ← Business logic, Service implementations
└── pos-api.sln
```

### Layer Responsibilities

| Layer | Responsibility |
|-------|---------------|
| **POS.API** | HTTP endpoints, routing, request/response |
| **POS.Domain** | Models, enums, exceptions, interfaces |
| **POS.Repository** | EF Core DbContext, repositories, Unit of Work |
| **POS.Services** | Business logic, orchestration |

---

## Domain Models

```csharp
// Matches Angular Product interface
Product { Id, Name, PriceCents, CategoryId, ImageUrl?, IsAvailable, Sizes[], Extras[] }

// Matches Angular Category interface
Category { Id, Name, Icon, SortOrder, IsActive }

// Offline sync from IndexedDB
Order { Id (UUID), OrderNumber, Items[], TotalCents, PaymentMethod, TenderedCents?, CreatedAt, SyncedAt, BusinessId, PaymentProvider? }

// Business configuration per tenant
Business { Id, Name, LocationName, PinHash }
```

---

## API Endpoints (MVP)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products` | All active products with sizes/extras |
| POST | `/api/products` | Create product |
| PUT | `/api/products/{id}` | Update product |
| PATCH | `/api/products/{id}/toggle` | Toggle active/inactive |
| GET | `/api/categories` | All active categories |
| POST | `/api/categories` | Create category |
| PUT | `/api/categories/{id}` | Update category |
| POST | `/api/orders/sync` | Sync offline orders from IndexedDB |
| GET | `/api/orders` | Orders with date filter |
| GET | `/api/orders/summary` | Daily KPIs |
| GET | `/api/business/config` | Business configuration |
| PUT | `/api/business/config` | Update business config |

---

## Architecture Patterns

### Repository + Unit of Work
```csharp
public interface IUnitOfWork : IDisposable {
    IProductRepository Products { get; }
    ICategoryRepository Categories { get; }
    IOrderRepository Orders { get; }
    IBusinessRepository Business { get; }
    Task<int> SaveChangesAsync();
}
```

---

## Naming Conventions

- **Files/Classes**: PascalCase — `ProductController.cs`
- **Interfaces**: Prefix `I` — `IProductService`
- **Methods**: PascalCase async — `GetAllActiveAsync()`
- **Private fields**: camelCase with `_` — `_productService`
- **Properties**: PascalCase — `PriceCents`

---

## Offline Sync Strategy

Frontend stores orders in IndexedDB when offline.
When online, POSTs to `/api/orders/sync`.

Sync endpoint rules:
1. Accept array of orders with client UUIDs
2. Idempotent — same UUID = skip duplicate
3. Return sync status per order
4. Never reject valid orders — log and continue

---

## Development Commands

```bash
dotnet build
dotnet run
dotnet ef migrations add <Name> --project POS.Repository --startup-project POS.API
dotnet ef database update --project POS.Repository --startup-project POS.API
```

---

## Implementation Process

Always follow `.claude/response-guidelines.md`:

1. **Analyze first** — read context before proposing code
2. **Wait for confirmation** — never implement without approval
3. **Implement only what was requested** — no extra features
4. **Summarize** — explain what was done, ask for feedback