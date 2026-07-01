# Modular Monolith with DDD Comparison

## 1. Purpose

Compare the current `ModulithReliabilityKit` skeleton and its Catalog sample against a well-known public
reference — **Kamil Grzybek's "Modular Monolith with DDD"
([github.com/kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd),
public reference)**. The goal is not to copy it wholesale, but to identify which ideas should improve
`ModulithReliabilityKit`, which ideas should stay as reference-only, and why.

## 2. What was inspected

From the public reference: its architecture decision log (ADRs on module facades, CQRS, 2-layered reads,
clean architecture for writes, event-driven communication between modules, per-module IoC containers, and
architecture tests) and its sample module (facade contract, module registration, processing/data-access
wiring, outbox processor, internal commands scheduler, a domain aggregate, a query handler, and its
architecture tests). All of the above are publicly available and attributed to Kamil Grzybek.

Current `ModulithReliabilityKit`:

- `src/Modules/Catalog/`
- `src/Api/ModulithReliabilityKit.Api/Program.cs`
- the API module endpoints (Catalog)
- the Building Blocks DI extension(s)
- `ModulithReliabilityKit.ArchitectureTests`

## 3. Current design summary

The public reference is a strong architectural reference because its ADRs are explicit and its module
boundaries are intentionally tested.

Its main pattern:

- API depends on a module facade, not directly on MediatR.
- Commands and queries are the facade input.
- Writes use Clean Architecture: API -> Application -> Domain, with Infrastructure behind abstractions.
- Reads use a simpler two-layer style: an Application query handler reads directly via Dapper.
- Modules communicate asynchronously through integration events.
- Each module owns an IoC container and composition root.
- Architecture tests enforce module/layer/domain rules.

`ModulithReliabilityKit` currently:

- API depends on `ICatalogModule`; MediatR is behind `CatalogModuleFacade`.
- Commands/queries are the module facade input.
- Writes follow a clean Application/Domain/Infrastructure split.
- Reads use an Application port (`IProductReadStore`) implemented in Infrastructure.
- Integration events are a separate public assembly.
- DI is MS.DI-first with a single host container; no per-module containers.
- Module persistence uses typed `IModuleUnitOfWork<TContext>` plus typed domain-event accessor/dispatcher registration.
- Architecture tests cover basic layer rules and the public `IntegrationEvents` boundary.

## 4. What to adopt

### 4.1 Module facade

**Reference strength:** the public reference exposes a module only through a small facade:

```csharp
Task<TResult> ExecuteCommandAsync<TResult>(ICommand<TResult> command);
Task ExecuteCommandAsync(ICommand command);
Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query);
```

This is a better API boundary than letting controllers/endpoints depend on raw MediatR. It makes the
module contract visible and keeps MediatR an internal implementation detail.

**Implemented in ModulithReliabilityKit by:** adding `ICatalogModule`, implemented in `Catalog.Infrastructure`, while
keeping `AddCatalogModule` MS.DI-first.

**Why:** it improves module encapsulation without reintroducing per-module containers. API code depends
on `ICatalogModule` + public command/query DTOs only; MediatR remains behind the facade.

### 4.2 Explicit CQRS policy

**Reference strength:** the reference's ADRs distinguish write complexity from read simplicity:

- writes justify Domain + Application + Infrastructure
- reads can be two-layer and optimized

**Improve ModulithReliabilityKit by:** documenting this as the default module rule:

- command handlers load aggregates and enforce behavior
- query handlers return DTOs and may use read-store ports, EF projections, or Dapper
- read models do not need aggregate purity

**Why:** this prevents over-modeling simple reads and keeps rich domain modeling where it pays off.

### 4.3 Stronger architecture tests

**Reference strength:** the reference's module arch tests go beyond "no EF in Domain." They check:

- domain events and value objects are immutable
- non-root entities do not expose public members
- entities do not hold direct references to other aggregate roots
- domain naming conventions (`DomainEvent`, `Rule`)
- modules do not depend on other modules except sanctioned integration handlers/startup points

**Improve ModulithReliabilityKit by:** expanding `ModulithReliabilityKit.ArchitectureTests` with:

- immutable `ValueObject` / `IDomainEvent` checks
- aggregate-root reference checks
- module-to-module dependency rules
- naming rules for `*DomainEvent`, `*Rule`, `*IntegrationEvent`
- one test class per module once the second module exists

**Why:** these rules catch architectural drift earlier than review.

### 4.4 Internal command / async operation pattern

**Reference strength:** the reference persists asynchronous commands for eventual background execution
(an internal-commands scheduler).

**Improve ModulithReliabilityKit by:** adding an internal-command abstraction only when there is a real delayed/retryable
module operation.

**Why:** this is useful for email, notifications, reconciliation, and long-running side effects. It should
not be part of the first lightweight module because it adds tables, scheduler policy, and failure semantics
before there is a concrete use case.

## 5. What not to adopt directly

### 5.1 Per-module IoC containers

**Reference choice:** the reference accepts one IoC container per module to maximize autonomy.

**Do not copy directly.** It gives strong runtime isolation but introduces static composition roots, duplicated
container modules, more lifecycle complexity, and non-standard hosting.

**Keep ModulithReliabilityKit direction:** one host container, MS.DI-first registration.

**Current substitute:** module facades plus typed module persistence adapters. That gives most of the
boundary benefit without multiple containers.

### 5.2 Application-layer direct Dapper as the only read pattern

**Reference choice:** read handlers depend directly on a SQL connection factory and Dapper.

**Do not copy as a blanket rule.** It is fast and simple, but couples Application to SQL shape and Dapper.

**Keep ModulithReliabilityKit direction:** Application owns read intent and DTOs; Infrastructure owns the read-store implementation.
Dapper can still be used behind `IProductReadStore` when needed.

**Why:** this preserves the option to use EF projections for simple reads and Dapper for hot paths without changing
use cases.

### 5.3 Reflection-heavy outbox notification mapping

**Reference choice:** the dispatcher resolves notification types through the container and maps notification
types to string names through a bidirectional dictionary.

**Do not copy directly.** It is powerful but hidden, container-specific, and harder to reason about.

**Keep ModulithReliabilityKit direction:** explicit notification mapping via `IDomainNotificationsMapper.Register<TDomainEvent>(...)`.

**How to improve:** add startup validation that every module domain event intended for outbox has an explicit mapping.

**Why:** explicit mapping is easier to audit and works with MS.DI and Autofac.

### 5.4 No explicit transaction in Unit of Work

**Reference choice:** the reference's unit-of-work dispatches domain events, then calls `SaveChangesAsync`.

**Do not copy directly.** EF will wrap `SaveChanges` in a transaction, but the boundary is implicit and harder to
extend when outbox/inbox writes, multiple contexts, or retries appear.

**Keep ModulithReliabilityKit direction:** explicit transaction in the unit-of-work.

**Why:** the transaction boundary is visible and reviewable.

## 6. Recommended improvement backlog

### Priority 1: Add module facade without changing DI topology

Status: implemented.

This adopts the strongest idea from the public reference while keeping MS.DI-first composition.

### Priority 2: Replace bare `DbContext` forwarding before module two

The current `CatalogModule` forwards `DbContext` to `CatalogContext`. That is acceptable for one module but becomes
last-wins with multiple modules.

Status: implemented with reusable Building Blocks services:

- `IModuleUnitOfWork<TContext>`
- `ModuleUnitOfWork<TContext>`
- typed domain-event accessor/dispatcher per context
- `AddModulePersistence<TContext>(requestAssembly)`
- `IUnitOfWorkResolver` request-assembly mapping

This is the correct substitute for per-module containers.

### Priority 3: Strengthen architecture tests

Port the useful domain/module tests from the public reference, adjusted for `ModulithReliabilityKit` conventions:

- immutable `ValueObject` and `IDomainEvent`
- no aggregate-root references inside entities
- domain object constructor rules, if they fit EF needs
- module dependency rules with `IntegrationEvents` as the only public cross-module assembly

### Priority 4: Add mapping validation for domain notifications

Keep explicit mapper registration, but fail startup if a module has domain notifications that should enter outbox
and are not mapped.

This keeps the safety of the reference's startup mapping check without copying container-specific reflection.

### Priority 5: Delay internal commands until needed

Do not add internal commands yet. Add them when a real side effect needs delayed/retryable processing.

## 7. Updated verdict

The current `Catalog` sample is directionally correct, but incomplete in one important boundary:
API still sees MediatR. The best next improvement is a module facade.

Keep:

- MS.DI-first, single-container design
- separate `IntegrationEvents` assembly
- explicit transaction UoW
- Application read-store port
- explicit domain notification mapping

Adopt:

- module facade
- stronger arch tests
- explicit CQRS documentation
- future internal command pattern

Reject:

- per-module static containers
- direct Dapper in Application as the default
- container-specific outbox reflection

## 8. Validation checklist

- API references only module contracts, not module Infrastructure or raw MediatR.
- Other modules reference only `*.IntegrationEvents`.
- Domain remains MediatR/EF/FluentValidation-free.
- Query path is simple but intentionally separated from aggregate behavior.
- Module persistence does not rely on a shared bare `DbContext` when multiple modules exist.
- Outbox mappings are explicit and validated at startup.
- Architecture tests enforce the rules, not just document them.
