# BuildingBlocks (Shared Kernel)

## Purpose

Document the reusable infrastructure in `src/BuildingBlocks/` — the abstractions every module depends on.
For each concept: what it is, where it lives, how modules depend on it, and whether it is worth copying
into a new project.

## Areas inspected

```text
src/BuildingBlocks/
  ModulithReliabilityKit.BuildingBlocks.Domain/
    Entity, ValueObject, IAggregateRoot
    domain-event + business-rule abstractions
    StronglyTypedId (typed-ID base)
  ModulithReliabilityKit.BuildingBlocks.Application/
    Events/ (IEventsBus, IntegrationEvent, domain-event notifications)
    Outbox/ (IOutbox, OutboxMessage)
    Inbox/  (inbox contracts, InboxRetryPolicy)
    execution context, exceptions, query/paging helpers
  ModulithReliabilityKit.BuildingBlocks.Infrastructure/
    Pipeline/UnitOfWorkBehavior, domain-event dispatching
    in-memory event bus
    Processing/OutboxProcessorBase (+ inbox processing)
    logging, DI extensions, typed-ID converters
```

## Layout: three projects mirroring the per-module layering

`BuildingBlocks` is split exactly the way each module is — `Domain`, `Application`, `Infrastructure`.
This keeps dependency direction clean: module `Domain` references `BuildingBlocks.Domain`, module
`Application` references `BuildingBlocks.Application`, etc.

---

## A. Domain abstractions — `BuildingBlocks.Domain`

### `Entity`

Base class for aggregate roots / entities. Holds a private list of domain events and exposes the
add/clear operations, plus a `CheckRule(IBusinessRule)` helper for invariant enforcement (a broken rule
throws a business-rule exception).

- **Depends on it:** every aggregate in every module (e.g. `Product` in Catalog).
- **VERDICT: copy.** Standard, correct DDD entity base.

### `ValueObject`

Value-object base providing structural equality.

- **VERDICT: copy-with-changes.** Prefer C# `record`-based value objects for explicit, fast equality; reserve
  a reflection-based base only where reflection equality is genuinely needed. Reflection-based equality is
  slower and easy to get subtly wrong.

### `IAggregateRoot`

Empty marker interface. **VERDICT: copy** (useful for generic constraints / arch tests).

### Domain events

A domain-event abstraction carrying an id + `OccurredOn`.

- **Coupling note:** a common shortcut is to make the domain-event interface derive from a mediation library
  (e.g. `INotification`), which bleeds an infrastructure concern into Domain. This kit keeps Domain free of
  MediatR and adapts at the Application/dispatch layer.
- **VERDICT: copy the id/`OccurredOn` shape; keep Domain mediation-free** and adapt at dispatch.

### Business rules

Explicit business-rule objects (`Message`, `IsBroken()`); broken rules throw a business-rule validation
exception.

- **VERDICT: copy.** Clean way to make invariants first-class and testable.

### Strongly-typed IDs (`StronglyTypedId`)

A single strongly-typed-ID style for the whole repo.

- **Lesson (from prior art):** avoid shipping **two** coexisting typed-ID styles (e.g. a legacy base class
  plus newer record-based IDs). A half-finished migration is pure debt.
- **VERDICT: pick one style and use it from day one.** This kit uses one.

---

## B. Application abstractions — `BuildingBlocks.Application`

### Events: `IEventsBus`, `IntegrationEvent`

The cross-module messaging contract. `IEventsBus` exposes publish/subscribe; `IntegrationEvent` is the
abstract base for public cross-module events (id, `OccurredOn`, version).

> ⚠️ **Lesson (from prior art):** do **not** ship the same event contracts in **two** namespaces (e.g. an
> Application copy and an Infrastructure "backward-compatibility" alias). That forces every reader to check
> which import a module uses and is debt, not design. This kit keeps a **single canonical definition** in
> Application.

- **VERDICT: single Application-layer definition.**

### Domain-event notifications

The bridge type between a domain event and the outbox. A *notification* wraps a domain event and is what
actually gets serialized to the outbox.

- **VERDICT: copy.** This three-level model (domain event → notification → integration event) is the core of
  the durable-eventing design. See `05-events-and-messaging`.

### Outbox: `IOutbox` / `OutboxMessage`

`IOutbox` exposes an `Add`; `OutboxMessage` carries a logical id (UUID), a sequential PK, `OccurredOn`, a
`type` string, a serialized payload, and a processed marker.

- **VERDICT: copy the message shape** (logical id + type/payload + processed marker). Keep the interface
  honest — don't ship interface methods that do nothing.

### Inbox contracts + `Inbox/InboxRetryPolicy.cs`

The consumer-side contracts, including the retry/backoff schedule in
`Inbox/InboxRetryPolicy.cs` and the inbox message shape (retry count, last error, next-retry time, status
∈ {pending | retrying | processed | dead_letter}), plus a dead-letter record with a resolution workflow.

- **VERDICT: copy.** The retry + dead-letter model is the mature core of reliable consumption.

### Execution context

An ambient request context (`UserId`, `CorrelationId`, availability flags).

- **Depends on it:** logging/correlation and any request-scoped policy.
- **VERDICT: copy the concept.** A first-class execution-context accessor is useful for correlation and
  request-scoped behavior. Keep any environment-specific fields out of the shared kernel.

### Exceptions & helpers

Standardized not-found and invalid-command exceptions (the latter thrown by the validation behavior), plus
paging contracts (`IPagedQuery`, `PageData`, paging helper).

- **VERDICT: copy.**

---

## C. Infrastructure abstractions — `BuildingBlocks.Infrastructure`

### Unit of Work — `Pipeline/UnitOfWorkBehavior.cs`

Detailed in `02-application-pipeline/unit-of-work.md`. Summary: the behavior dispatches domain events then
saves, inside an **explicit transaction**. **VERDICT: copy.**

### Domain-event dispatching

- A domain-events accessor pulls domain events out of the EF `ChangeTracker`.
- A dispatcher publishes domain events in-process **and** serializes matching notifications into the outbox.
- An explicit notification mapper maps a `type` string ↔ notification type for (de)serialization.

- **VERDICT: copy.** This is the heart of the design. Prefer **explicit** notification mapping (register each
  mapping) over reflection/container-scan mapping — it is easier to audit and container-agnostic.

### Event bus (in-memory)

An in-memory event-bus implementation bound to `IEventsBus`. **This is the only transport.** No durable
broker implements `IEventsBus`.

- **VERDICT: fine as a default/dev transport; do not treat it as durable cross-module delivery.** Durability
  comes from the **outbox + inbox tables**, not the bus. This kit's in-memory bus does **not** silently
  swallow publish exceptions by default.

### Processing — `Processing/OutboxProcessorBase.cs`

A reusable base for outbox draining (fetch a batch of unprocessed rows, publish each, mark processed per
row — mark-after-publish) that concrete module processors extend. Inbox processing follows the same
batched shape with retry + dead-letter.

- **VERDICT: copy.** Small, reusable across modules. A single background drainer runs today; adding
  `FOR UPDATE SKIP LOCKED` to the fetch is the hardening step if multiple drainers ever run concurrently.

### Other infrastructure

- Logging behavior (one mechanism, applied uniformly). **Copy.**
- Typed-ID EF value converters. **Copy** if you adopt typed IDs.
- DI extension(s) for registering the shared infrastructure and typed module persistence. **Copy.**

---

## Dependency direction (how modules depend on BuildingBlocks)

```text
Module.Domain            → BuildingBlocks.Domain
Module.IntegrationEvents → BuildingBlocks.Application
Module.Application       → Module.Domain + Module.IntegrationEvents + BuildingBlocks.Application
Module.Infrastructure    → Module.Application + BuildingBlocks.Infrastructure (+ BuildingBlocks.Application)
```

This is clean and worth replicating. Architecture tests enforce it (see `07-module-architecture`).

## What to copy (summary)

- Domain primitives: `Entity`, `IAggregateRoot`, domain-event base, `IBusinessRule` + exception.
- The **domain event → notification → integration event** model and the dispatcher (with explicit mapping).
- Outbox/inbox **message shapes** with a processed marker + retry/dead-letter fields.
- Execution context, paging helpers, standardized exceptions.
- Typed-ID EF converters (if adopting typed IDs).

## What not to copy blindly

- **Duplicated event contracts across two namespaces** — keep one canonical Application definition.
- **Two coexisting strongly-typed-ID styles** — choose one.
- The **in-memory bus** as your durable cross-module transport.
- **Mediation-library coupling in the Domain layer** — keep Domain pure and adapt at dispatch.
- Interface methods that do nothing (e.g. a no-op outbox `Save()`).

## Open questions

- Which execution-context fields belong in the shared kernel vs. a module concern?
- Should the read-model/consumer side reuse the same building blocks, or a leaner subset?

## Next

- `01-foundation/dependency-injection.md`
- `02-application-pipeline/unit-of-work.md`
