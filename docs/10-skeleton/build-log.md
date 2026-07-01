# Skeleton Build Log

## Purpose

Track what was created in each implementation phase for the `ModulithReliabilityKit` skeleton.

## Phase 1

Completed:

- Created solution and root build files:
  - `src/ModulithReliabilityKit.sln`
  - `src/global.json`
  - `src/Directory.Build.props`
  - `src/Directory.Packages.props`
- Created and wired projects:
  - `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Domain`
  - `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Application`
- Implemented Domain baseline:
  - `Entity`, `ValueObject`, `StronglyTypedId<TValue>`, `IDomainEvent`, `DomainEventBase`,
    `IBusinessRule`, `IAsyncBusinessRule`, `BusinessRuleValidationException`, `IAggregateRoot`
- Implemented Application baseline:
  - Commands/Queries contracts
  - Domain event notifications
  - Integration event contracts + reliability marker
  - Outbox/Inbox message contracts
  - `IUnitOfWork`, `IExecutionContextAccessor`
  - `EntityNotFoundException`, `InvalidCommandException`
  - Paging helpers
- Added docs chapter:
  - `docs/10-skeleton/implementation-decisions.md`
  - `docs-tw/10-skeleton/implementation-decisions.md`
  - This build log (`docs` + `docs-tw`)

## Phase 2

Completed:

- Added and wired `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure`
- Implemented:
  - explicit-transaction `UnitOfWork`
  - domain events dispatching services (`DomainEventsAccessor`, `DomainEventsDispatcher`, mapper)
  - MediatR pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`, `UnitOfWorkBehavior`)
  - non-swallowing `InMemoryEventBus`
  - reusable `OutboxProcessorBase` (the consumer side later moved to a transaction-safe `NotificationsInboxProcessor` instead of a shared base)
  - strongly-typed-id EF converter helpers
  - DI extension `AddModulithReliabilityKitBuildingBlocks(IServiceCollection)`
  - JSON defaults helper (`System.Text.Json`)
- Added outbox/inbox SQL templates:
  - `docs/10-skeleton/outbox-inbox-schema-template.sql`
  - `docs-tw/10-skeleton/outbox-inbox-schema-template.sql`
- Verified solution build after phase 2.

Pending:

- Phase 3: API host + architecture tests + final verification

## Phase 3

Completed:

- Added API host:
  - `src/Api/ModulithReliabilityKit.Api`
  - Serilog, Swagger/OpenAPI, `/health`, root endpoint, module registration seam comment
  - `AddModulithReliabilityKitBuildingBlocks(includePersistenceServices: false)` to allow host boot without module DbContext
- Added architecture tests:
  - `src/Tests/ModulithReliabilityKit.ArchitectureTests`
  - Rules: Domain must not depend on MediatR/EF Core; Application must not depend on Infrastructure
- Verification run:
  - `dotnet build src/ModulithReliabilityKit.sln` passed
  - `dotnet run` + `curl /health` returned `Healthy`
  - `dotnet test src/ModulithReliabilityKit.sln` passed (3/3)

Pending:

- None for this plan scope.

## Phase 4

Completed:

- Applied improvement #1 (module facade):
  - Added `ICatalogModule` contract
  - Added `CatalogModuleFacade`
  - Switched API endpoints to call `ICatalogModule` (no raw `ISender` in endpoint boundary)
- Applied improvement #2 (typed module persistence adapter):
  - Added `CatalogUnitOfWork`
  - Added `CatalogDomainEventsAccessor`
  - Removed bare `DbContext` forwarding from Catalog module registration
- Added PostgreSQL minimal demo with migration-based schema creation:
  - Added `docker-compose.postgres.yml`
  - Added local tool manifest + `dotnet-ef`
  - Added EF design-time support
  - Added initial Catalog migration files
  - Added startup migration hosted service
- Added documentation:
  - `docs/10-skeleton/applied-improvements-and-postgres-demo.md`
  - `docs-tw/10-skeleton/applied-improvements-and-postgres-demo.md`

Verification run:

- `dotnet build src/ModulithReliabilityKit.sln --no-restore --disable-build-servers /m:1` passed
- `dotnet test src/Tests/ModulithReliabilityKit.ArchitectureTests/ModulithReliabilityKit.ArchitectureTests.csproj --no-build` passed (7/7)
- PostgreSQL container started and reached healthy
- Migration applied and `catalog.products` / `catalog.outbox_messages` verified
- API smoke test (create/get product) succeeded

## Phase 5

Completed:

- Applied Priority 2 full reusable module persistence:
  - Added `IModuleUnitOfWork<TContext>` and reusable `ModuleUnitOfWork<TContext>`
  - Added typed `IDomainEventsAccessor<TContext>` / `DomainEventsAccessor<TContext>`
  - Added typed `IDomainEventsDispatcher<TContext>` / `DomainEventsDispatcher<TContext>`
  - Added `AddModulePersistence<TContext>(requestAssembly)` for module registration
  - Added request-assembly based `IUnitOfWorkResolver` so multiple module contexts can coexist in one root container
- Updated Catalog to use the reusable Building Blocks adapters:
  - Removed `CatalogUnitOfWork`
  - Removed `CatalogDomainEventsAccessor`
  - Replaced custom persistence wiring with `services.AddModulePersistence<CatalogContext>(CatalogApplicationAssembly.Assembly)`

Verification run:

- `dotnet build src/ModulithReliabilityKit.sln --no-restore --disable-build-servers /m:1` passed
- `dotnet test src/Tests/ModulithReliabilityKit.ArchitectureTests/ModulithReliabilityKit.ArchitectureTests.csproj --no-build` passed (7/7)
- API smoke test (create/get product) succeeded through the typed module UoW
