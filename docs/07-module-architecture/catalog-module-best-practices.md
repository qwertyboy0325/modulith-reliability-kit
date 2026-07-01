# Catalog Module Best Practices

## 1. Purpose

This chapter documents the first concrete producer module in the `ModulithReliabilityKit` skeleton:
`src/Modules/Catalog/`. It is intentionally light in domain complexity, but complete in
architectural coverage: Domain, Application, IntegrationEvents, Infrastructure, API endpoint
wiring, outbox, background processing, and architecture tests.

## 2. What was inspected

Public prior art: **Kamil Grzybek's "Modular Monolith with DDD" (public reference)** — its module facade,
per-module startup/registration, EF context + repository + entity-type configuration style, and public
integration-event contracts. Attributed as such; see `modular-monolith-ddd-comparison.md`.

New implementation (this kit):

- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Domain/`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Application/`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.IntegrationEvents/`
- `src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/`
- `src/Api/ModulithReliabilityKit.Api/Program.cs` + the Catalog API endpoints
- `ModulithReliabilityKit.ArchitectureTests`

## 3. Actual implementation summary

The reference per-module pattern is useful, but not copied literally.

**ACTUAL in the reference:** each module can own its EF context, repositories, outbox, scheduler,
integration events, and a module-specific composition root, building its own IoC container per module.

**VERDICT:** copy the module boundary and completeness; do not copy per-module containers.

`Catalog` keeps the good parts:

- one module folder with four assemblies: `Domain`, `Application`, `IntegrationEvents`, `Infrastructure`
- `Domain` has no MediatR, FluentValidation, EF Core, or infrastructure dependency
- `Application` owns use cases, validators, query ports, notification handlers, and outbox enqueue logic
- `IntegrationEvents` is the only public cross-module contract assembly
- `Infrastructure` owns EF Core, repository implementations, read store, outbox storage, outbox processor, and module DI
- API only maps HTTP to MediatR requests; it does not contain module business logic

## 4. Module shape

```text
src/Modules/Catalog/
  ModulithReliabilityKit.Modules.Catalog.Domain/
    Products/
      Product.cs
      ProductId.cs
      Money.cs
      Rules/
      Events/
      IProductRepository.cs
  ModulithReliabilityKit.Modules.Catalog.Application/
    Products/
      CreateProduct/
      RenameProduct/
      GetProduct/
      Events/
  ModulithReliabilityKit.Modules.Catalog.IntegrationEvents/
    ProductCreatedIntegrationEvent.cs
  ModulithReliabilityKit.Modules.Catalog.Infrastructure/
    CatalogContext.cs
    Configuration/CatalogModule.cs
    Domain/Products/
    Outbox/
    Processing/
```

The important direction is:

```text
Infrastructure -> Application -> Domain
Application -> IntegrationEvents
Infrastructure -> IntegrationEvents
Other modules -> IntegrationEvents only
```

No module should reference another module's `Domain`, `Application`, or `Infrastructure`.

## 5. Request flow

Create product:

```text
HTTP POST /catalog/products
  -> CatalogEndpoints
  -> ICatalogModule.ExecuteCommandAsync(CreateProductCommand)
  -> ISender.Send(CreateProductCommand) inside CatalogModuleFacade
  -> ValidationBehavior
  -> LoggingBehavior
  -> CreateProductCommandHandler
  -> Product.Create(...)
  -> IProductRepository.AddAsync(...)
  -> UnitOfWorkBehavior
  -> IUnitOfWorkResolver
  -> ModuleUnitOfWork<CatalogContext>
  -> DomainEventsDispatcher<CatalogContext>
  -> ProductCreatedNotificationHandler
  -> IOutbox.Add(ProductCreatedIntegrationEvent payload)
  -> DbContext.SaveChangesAsync()
  -> commit
  -> CatalogOutboxProcessor (background)
  -> IEventsBus.Publish(ProductCreatedIntegrationEvent)
```

This keeps the aggregate write and outbox insert in the same database transaction. The bus publish
happens after commit, so the module avoids the dual-write problem.

## 6. What to copy

Copy the four-assembly module split. It is small enough to understand and strict enough to enforce
module boundaries.

Copy the separate `IntegrationEvents` assembly. It lets other modules subscribe to public contracts
without referencing the module internals.

Copy the Application-layer outbox enqueue handler. Domain events remain in-process facts; integration
events become durable public messages.

Copy the API endpoint style. Endpoints translate transport input into commands/queries and return
HTTP results. They do not load EF contexts, repositories, or aggregates directly.

Copy the architecture tests. The tests now enforce that module Domain does not depend on MediatR,
EF Core, or FluentValidation; Application does not depend on Infrastructure; and IntegrationEvents
does not leak module internals.

## 7. What not to copy blindly

Do not copy the reference's per-module IoC container. In `ModulithReliabilityKit`, module registration
is MS.DI-first through `AddCatalogModule`.

Do not publish integration events directly inside command handlers. Put them into the outbox from a
domain notification handler and let the processor publish after commit.

Do not let queries depend on repositories by default. `GetProductQueryHandler` uses `IProductReadStore`
so the read path can later move to Dapper/projections without changing use cases.

Do not reintroduce bare `DbContext` forwarding. Each module should register typed persistence through
the reusable Building Blocks adapter:

```csharp
services.AddModulePersistence<CatalogContext>(CatalogApplicationAssembly.Assembly);
```

This registers `IModuleUnitOfWork<CatalogContext>`, typed domain-event accessor/dispatcher services,
and a request-assembly mapping used by `IUnitOfWorkResolver`. That keeps one root container while
letting multiple module contexts coexist without last-wins registration.

## 8. Best-practice rules for future modules

1. Start with `IntegrationEvents` as the public API and keep it stable.
2. Keep domain events private to the module; translate them to integration events in Application.
3. Keep repositories write-side only unless the aggregate must be loaded for behavior.
4. Put query DTO/read-store ports in Application and implementations in Infrastructure.
5. Register module services through one `Add<ModuleName>Module` extension.
6. Keep module startup MS.DI-first by avoiding container-specific APIs.
7. Add architecture tests for every new module before adding a second module.
8. Use outbox for durable cross-module events; direct bus publish is only for explicitly best-effort signals.

## 9. Open questions

- Should outbox polling remain a hosted background service or move to a scheduler as the system grows?
- Should read stores use Dapper immediately, or keep EF projections until performance requires otherwise?

## 10. Next document links

- `docs/10-skeleton/implementation-decisions.md`
- `docs/07-module-architecture/modular-monolith-ddd-comparison.md`
- `docs/05-events-and-messaging/integration-events.md`
- `docs/02-application-pipeline/unit-of-work.md`
