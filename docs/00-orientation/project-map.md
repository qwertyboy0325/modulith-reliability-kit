# Project Map

## Purpose

Map the major source folders of the `ModulithReliabilityKit` skeleton (`src/`) and state what each one
*owns*. This is about **project shape**, not business features. Read this before anything else.

## Source folders inspected

- `src/` (solution layout)
- `src/BuildingBlocks/`
- `src/Modules/`
- `src/Api/ModulithReliabilityKit.Api/`
- `src/Tests/`

## The `src/` layout

```text
src/
  Api/ModulithReliabilityKit.Api/   → Composition host (ASP.NET Core). Owns startup & module wiring.
  BuildingBlocks/                   → Shared kernel: Domain/Application/Infrastructure abstractions reused by all modules.
    ModulithReliabilityKit.BuildingBlocks.Domain/
    ModulithReliabilityKit.BuildingBlocks.Application/
    ModulithReliabilityKit.BuildingBlocks.Infrastructure/
  Modules/                          → Bounded contexts, each a vertical slice with its own layer split.
    Catalog/        → producer module (aggregate Product; publishes ProductCreatedIntegrationEvent)
    Notifications/  → consumer module (read model ProductAnnouncement; inbox-based consume)
  Tests/                            → Cross-cutting tests: ArchitectureTests, IntegrationTests, ReliabilityTests.
```

## What each area owns

### API host — `src/Api/ModulithReliabilityKit.Api/`

- **Owns:** the process entry point, HTTP endpoints, ASP.NET Core middleware, and the
  **composition of all modules**.
- **Key files:** `Program.cs` (host + DI composition), `Modules/CatalogEndpoints.cs` (HTTP → command/query mapping).
- **Note:** the API is the only place that references each module's `Infrastructure` project, and it calls
  each module's `Add<Module>Module(...)` MS.DI registration extension to wire the module in.

### BuildingBlocks — `src/BuildingBlocks/`

The shared kernel. Three projects, mirroring the layering every module repeats:

- `BuildingBlocks.Domain/` — base classes for the domain model: `Entity`, `ValueObject`,
  `IAggregateRoot`, domain-event and business-rule abstractions, strongly-typed-ID base.
- `BuildingBlocks.Application/` — application-layer contracts: `IEventsBus`, `IntegrationEvent`,
  domain-event notifications, `IOutbox`/`OutboxMessage`, inbox contracts + retry policy, execution
  context, exceptions, query/paging helpers.
- `BuildingBlocks.Infrastructure/` — concrete cross-cutting infrastructure: the unit-of-work behavior,
  domain-event dispatching, the in-memory event bus, outbox/inbox processor bases, logging, and DI extensions.

Detailed in `01-foundation/building-blocks.md`.

### Modules — `src/Modules/<Module>/`

Two bounded contexts today: a **Catalog** producer and a **Notifications** consumer.

The producer (Catalog) uses a four-layer split:

```text
Catalog/
  Domain/            → aggregates (Product), value objects, domain events, repository interfaces, business rules
  Application/       → commands, queries, handlers, validators, notification handlers, outbox enqueue
  IntegrationEvents/ → the module's PUBLIC integration-event contracts (the only thing other modules may reference)
  Infrastructure/    → DbContext, EF repository implementations, DI wiring, outbox processor
```

The consumer (Notifications) is intentionally lighter: an `Application` layer (read model, handlers) and an
`Infrastructure` layer (inbox writer, inbox processor, DbContext). It does not publish its own contracts,
so it has no `IntegrationEvents` project.

> ACTUAL: cross-module references are limited to another module's `IntegrationEvents` project.
> No module references another module's Domain/Application/Infrastructure (good). Architecture tests
> enforce this boundary — see `07-module-architecture`.

### Infrastructure (cross-cutting) — split between BuildingBlocks and each module

There is **no single `Infrastructure` project**. Infrastructure exists at two levels:

1. **Shared** in `BuildingBlocks.Infrastructure/` (unit-of-work behavior, event bus, dispatching,
   outbox/inbox processor bases).
2. **Per-module** in `<Module>.Infrastructure/` (that module's DbContext, repositories, outbox/inbox handlers, DI extension).

### Application — split between BuildingBlocks and each module

Same pattern: shared application abstractions in `BuildingBlocks.Application/`,
module-specific commands/queries/handlers in `<Module>.Application/`.

### Domain — split between BuildingBlocks and each module

Shared domain primitives in `BuildingBlocks.Domain/`; the actual aggregates live in `<Module>.Domain/`
(e.g. `Product` in Catalog).

### IntegrationEvents — per producer module

The cross-module contract surface. A producer module has `<Module>.IntegrationEvents/` as a **separate project**
so other modules can depend on the contracts without depending on the implementation
(e.g. `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`).

### Tests — `src/Tests/`

- `ArchitectureTests` — architecture/boundary tests (layer direction, module isolation, public-contract rules).
- `IntegrationTests` — cross-module / infra integration tests (e.g. outbox → bus → inbox flow).
- `ReliabilityTests` — tests focused on the messaging reliability guarantees (retry, dead-letter, idempotency).

## Mental model (one diagram)

```text
                 ┌──────────────────────────────────────────────┐
                 │  API host (ModulithReliabilityKit.Api)         │
                 │  single container + Add<Module>Module wiring   │
                 └───────────────┬──────────────────────────────┘
                                 │ registers each module
             ┌───────────────────┴───────────────────┐
             ▼                                         ▼
      ┌──────────────┐   ProductCreated        ┌──────────────────┐
      │  Catalog     │ ─── integration event ─►│  Notifications   │
      │ D/A/IE/Infra │   (outbox → bus → inbox)│  A/Infra         │
      └──────┬───────┘                          └────────┬─────────┘
             └───────────────────┬───────────────────────┘
                                 │ all depend on
                                 ▼
                        ┌───────────────────┐
                        │  BuildingBlocks    │  Domain / Application / Infrastructure
                        └───────────────────┘
```

## What to copy

- The **`BuildingBlocks` + `Modules/<context>` + thin API host** shape is a clean, proven modular-monolith layout.
- The **layer-per-module split with a separate `IntegrationEvents` contract project** (on producer modules)
  is a strong boundary mechanism worth copying.
- **Central package/version management** (`Directory.Packages.props`).

## What not to copy blindly

- Do not add an `IntegrationEvents` project to a pure consumer module that publishes nothing (Notifications
  has none by design). Ship contracts only where they exist.
- Do not let a consumer module grow a full four-layer split before it needs one; keep it as light as its role.

## Open questions

- When a third module appears, does the two-module producer/consumer split still describe the topology, or
  does a hub/mesh emerge?
- Should the read model in Notifications get its own `Domain` project, or stay an Application/Infrastructure concern?

## Next

- `00-orientation/source-code-reading-order.md` — the order to actually read these folders.
- `01-foundation/building-blocks.md` — the shared kernel in detail.
