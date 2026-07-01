# Unit of Work

## Purpose

Explain *exactly* what Unit of Work means in this kit. The implementation is small; this document
distinguishes it from the textbook pattern and from a common implicit-transaction shortcut.

## Files / areas inspected

- `src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`
- The domain-events dispatcher + accessor in `BuildingBlocks.Infrastructure`
- The typed module unit-of-work + resolver in `BuildingBlocks.Infrastructure`
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs` (outbox enqueue)

## What it does

The unit-of-work is a MediatR pipeline behavior (`Pipeline/UnitOfWorkBehavior.cs`) that wraps command
handlers. After the inner handler returns successfully, it:

1. **Dispatches domain events** — pulls domain events from the EF `ChangeTracker`, publishes them
   in-process, and serializes the matching notifications into the outbox (adding outbox rows to the
   **same** `DbContext`).
2. **Saves + commits inside an explicit transaction** — persists **everything** (aggregate changes +
   outbox rows) and commits one transaction.

Because it resolves the right module `DbContext` per request (via a typed unit-of-work resolver), multiple
module contexts coexist without last-wins registration.

## Answering the required questions

### Does it explicitly manage database transactions?

**Yes.** This kit opens an **explicit transaction** in the unit-of-work, rather than relying only on EF's
implicit single-`SaveChanges` transaction.

> Lesson (from prior art): a common shortcut is to skip the explicit transaction and lean on the fact that
> EF Core wraps one `SaveChanges` in one transaction. That works — *until* a command writes through both EF
> and a second path (e.g. raw SQL/Dapper). Then atomicity silently breaks. Making the transaction explicit
> keeps the boundary visible and reviewable, and lets you enrol additional writes in it deliberately.

### How does it interact with `DbContext`?

It uses a single request-scoped `DbContext` (one per command). The domain-events accessor reads tracked
entities from that context's `ChangeTracker`, and the outbox enqueue adds outbox rows to the same context.
One context, one save, one transaction.

### How is it invoked?

Never directly by handlers. It is the pipeline behavior wrapping command handlers. A typical handler
mutates aggregates via repositories and returns; it does **not** call `SaveChanges`. The behavior commits.

If the handler throws (including a business-rule or validation exception from an earlier behavior), the
commit is never reached and nothing is persisted.

### What is the real consistency boundary?

**One command = one `DbContext` = one explicit transaction**, covering:

- all aggregate state changes made during the command, **and**
- all outbox rows produced from this command's domain events.

This gives the **transactional outbox guarantee**: if the command commits, the outbox messages are
committed atomically with it; if it rolls back, no outbox messages leak. Delivery to other modules then
happens later, asynchronously, via the outbox processor.

```text
Command handler runs
   ├─ repo.Add/Update(aggregate)        (tracked in DbContext)
   └─ aggregate.AddDomainEvent(...)     (in-memory on entity)
        │
   UnitOfWorkBehavior (after handler)
        ├─ dispatch domain events
        │     ├─ publish in-process
        │     └─ serialize notifications → IOutbox.Add(...)   (tracked in DbContext)
        └─ SaveChanges + commit  ← ONE explicit transaction: aggregate + outbox rows
```

### What are the trade-offs / limitations?

1. **In-process dispatch vs. save ordering.** Domain events are dispatched as part of commit. If an
   in-process domain-event handler performs side effects on a *different* store, those are not covered by
   this transaction — keep in-process handlers on the same context, or dispatch after a successful save.
2. **Explicit transaction adds a little boilerplate and more failure paths to test** — but the boundary is
   auditable, which is the point.
3. **Don't ship no-op interface methods** (e.g. an outbox `Save()` that does nothing) — they mislead.

## Textbook Unit of Work vs. this implementation

| Aspect | Textbook UoW | This kit |
| ------ | ------------ | -------- |
| Tracks changes | Maintains its own identity map / change set | Delegates to EF `ChangeTracker` |
| Transaction | Opens/commits an explicit transaction | **Explicit transaction** in the behavior |
| Commit scope | "all registered changes" | "everything tracked by the one DbContext for this command" |
| Domain events | Often dispatched after commit | Dispatched as part of commit (notifications serialized into the same save) |
| Outbox | Optional | Core: outbox rows written in the same transaction (transactional outbox) |
| Multiple data stores | Can coordinate | Single EF context per command; additional writes must enrol explicitly |

The honest summary: this is **"EF SaveChanges + domain-event dispatch + transactional outbox, inside an
explicit transaction"**, packaged as a pipeline behavior. It is a good *transactional outbox*; it is not a
general multi-resource UoW.

## What to copy

- The **transactional-outbox-via-one-transaction** idea: write aggregate + outbox atomically.
- The **behavior-invokes-commit** pattern so handlers never call `SaveChanges`.
- The **explicit transaction** boundary.
- Dispatching domain events as part of commit.

## What not to copy blindly

- Leaving the **transaction implicit.** If any command ever writes through both EF and another path, the
  implicit approach silently breaks atomicity.
- Publishing in-process domain events with external side effects **before** the save is durable.
- Shipping an outbox `Save()` (or similar) that does nothing.

## Open questions

- Are there commands that need to write via **both** EF and a second store? If so, define how both enrol in
  the transaction (or forbid mixing within a command).
- Should in-process domain-event handlers be allowed to perform writes on the same context, and are they?

## Next

- `05-events-and-messaging/integration-events.md`
- `05-events-and-messaging/reliability-matrix.md`
