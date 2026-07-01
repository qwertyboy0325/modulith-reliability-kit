# Dependency Injection & Composition Root

## Purpose

Explain the DI strategy of this kit: container topology, module-level registration, lifetime scopes,
the command pipeline, and how module dependencies are isolated vs. shared. This is one of the most
important structural decisions in a modular monolith, and it is easy to over-engineer.

## Files / areas inspected

- `src/Api/ModulithReliabilityKit.Api/Program.cs`
- `src/Api/ModulithReliabilityKit.Api/Modules/CatalogEndpoints.cs`
- Each module's `Add<Module>Module(...)` registration extension (in `<Module>.Infrastructure`)
- `BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`

## Headline: a single container with module-scoped registration

This kit is a **single-container** application. The ASP.NET Core host owns one MS.DI container, and each
module contributes its registrations through an `Add<Module>Module(...)` extension called from
`Program.cs`. Module boundaries are enforced by **project references + architecture tests**, not by
running each module in its own container.

```text
HTTP request
  → Endpoint (host container)
    → I<Module>Module facade
      → MediatR Send(command) inside a per-request scope
        → pipeline behaviors + handler resolved from the single container
```

### Design note: single container vs. per-module containers

A well-known alternative (used by some public modular-monolith references) is to give **each module its
own IoC container** and share only a few object instances between the host and the modules. That maximizes
runtime isolation, but it introduces static composition-root holders, duplicated container modules, more
lifecycle complexity, and non-standard hosting.

> ⚠️ **Anti-patterns to avoid** (observed in prior art, deliberately *not* copied here):
> - **Static-mutable container holders** (a per-module `static IContainer` set at startup). They are global
>   singletons that make parallel test isolation hard and hide the dependency graph.
> - **Leaked lifetime scopes** — opening a scope (`BeginLifetimeScope()`) to resolve a service without a
>   `using`, so the scope is never disposed.
>
> This kit chose a single container to sidestep both. Adopt per-module containers only if you have a hard
> runtime-isolation requirement you can name.

---

## 1. The host composition root

`Program.cs` builds the host and composes every module by calling its registration extension. Endpoints
are thin: they map transport input to a command/query and forward it through the module facade
(`ICatalogModule`), so the API never depends on MediatR directly.

The host owns cross-cutting singletons (the event bus, connection configuration) and passes configuration
into each module's `Add<Module>Module(...)`.

---

## 2. Per-module registration

Each module's `Add<Module>Module(...)` extension registers that module's own services — its `DbContext`,
repositories, handlers, outbox/inbox processing, and typed persistence — into the shared container.
Because there is one container, there is no per-module composition-root holder and no separate lifetime
tree to reason about.

Module persistence is registered through a reusable Building Blocks adapter
(`AddModulePersistence<TContext>(...)`), so multiple module `DbContext`s can coexist without last-wins
registration. See `07-module-architecture/catalog-module-best-practices.md`.

---

## 3. The command pipeline (MediatR pipeline behaviors)

State-changing commands flow through an ordered set of MediatR `IPipelineBehavior<,>`:

**Validation → UnitOfWork → Logging → handler.**

- **Validation** runs first (FluentValidation): if any validator fails, it throws before the handler runs.
- **UnitOfWork** (`Pipeline/UnitOfWorkBehavior.cs`) wraps the handler and commits after it returns
  successfully — dispatching domain events and persisting aggregate changes + outbox rows in one explicit
  transaction (see `02-application-pipeline/unit-of-work.md`).
- **Logging** records request name/type/elapsed and errors.

> Lesson (from prior art): keep the pipeline **uniform across modules**. Divergent per-module decorator
> sets, or two parallel logging mechanisms (a pipeline behavior in one module vs. a handler decorator in
> another), are debt. One pipeline, applied everywhere.

Handlers and validators are discovered by assembly scan per module.

---

## 4. Lifetime scopes (evidence)

| Service | Lifetime |
| ------- | -------- |
| Module `DbContext` (e.g. `CatalogContext`) | Scoped (one per request/command) |
| Repositories | Scoped |
| Unit-of-work behavior / domain-events dispatcher | Scoped |
| MediatR `IMediator` | Scoped |
| Outbox/inbox processors (hosted background work) | Singleton host + scoped work unit |
| `IEventsBus` (in-memory) | Singleton |

A **fresh scope per command/query** with `DbContext` registered **Scoped** means **one `DbContext` per
operation** — which is what makes the Unit of Work boundary equal to "one command".

---

## 5. Isolation vs. sharing

**Owned per module:** each module's `DbContext`, repositories, handlers, and outbox/inbox processing.
Modules do not reference each other's Domain/Application/Infrastructure — only another module's
`IntegrationEvents` contracts.

**Shared:** the in-memory `IEventsBus`, connection configuration, and the Building Blocks abstractions.

Consequences:
- Boundaries are enforced by **project references + architecture tests**, which is simpler than a container
  hierarchy and still catches violations in CI.
- There is no hidden global state from static container holders.

---

## What to copy

- **Single container + `Add<Module>Module` registration extensions** — simple, standard hosting.
- **MediatR pipeline behaviors** for the command pipeline (validation / unit-of-work / logging) — the core reusable idea.
- **One scope per command/query**, `DbContext` registered Scoped.
- **Typed module persistence** (`AddModulePersistence<TContext>`) so multiple contexts coexist cleanly.

## What not to copy blindly

- **Static-mutable composition-root holders** — global singletons that break test isolation and hide the graph.
- **Scope leaks** (`BeginLifetimeScope()` without `using`).
- **Per-module divergence** in pipeline registration — standardize it.
- **Two logging mechanisms** (pipeline behavior vs decorator) — pick one.
- The **per-module container model** by default. It is heavy and leaks easily. Prefer a single container with
  module-scoped registrations + arch tests; only adopt per-module containers with a named isolation requirement.

## Open questions

- When does module isolation warrant more than project references + arch tests?
- Should any background processing need its own scope factory beyond the per-command scope?

## Next

- `02-application-pipeline/unit-of-work.md`
- `05-events-and-messaging/integration-events.md`
