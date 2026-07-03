# Integration Events

## Purpose

Explain how integration events (cross-module messages) are triggered, transported, and consumed. There are
**two distinct publish paths** and an in-memory transport. The reliability of each path differs sharply.
This document distinguishes them with concrete examples from this kit.

## Files / areas inspected

- The `IEventsBus` / `IntegrationEvent` contracts in `BuildingBlocks.Application`
- The in-memory event bus in `BuildingBlocks.Infrastructure`
- The domain-events dispatcher in `BuildingBlocks.Infrastructure`
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs` (Path A publisher)
- `Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs` (outbox processor)
- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs` (public contract)
- `Notifications.Infrastructure/Inbox/InboxWriter.cs` (idempotent inbox ingest)
- `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs` (inbox processor)
- `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs` (retry/backoff)

## The transport: in-memory only

`IEventsBus` is bound to a **single process-wide in-memory bus** created at startup and shared by every
module. Two reliability-defining behaviors:

1. **No subscribers → the publish has no effect** (nothing consumes it).
2. **Publish exceptions** — in this kit the in-memory bus does **not** silently swallow publish exceptions
   by default; failures surface rather than being hidden.

> ⚠️ There is **no durable message broker** behind `IEventsBus`. Cross-module integration events are
> in-process only; durability comes solely from the **outbox/inbox tables**, not the bus. If modules might
> ever run as separate processes, put a broker behind the `IEventsBus` abstraction.

---

## Path A — Domain Event → Outbox → Outbox Processor → Notification Handler → `IEventsBus.Publish`

```text
Aggregate raises DomainEvent
  → (UnitOfWorkBehavior commit) DomainEventsDispatcher
        → maps DomainEvent → notification
        → serializes notification → OutboxMessage  (saved atomically with aggregate, one transaction)
  → CatalogOutboxProcessor (background)
        → fetch a batch of unprocessed rows (WHERE processed_on_utc IS NULL, ORDER BY id, LIMIT 50)
        → publish notification in-process
              → notification handler (ProductCreatedNotificationHandler)
                    → IEventsBus.Publish(IntegrationEvent)
        → mark outbox row processed (processed_on_utc = now)   ← mark-after-publish
```

### Step 1 — notification → outbox (in the dispatcher)

During commit, the dispatcher serializes each domain-event notification into an `OutboxMessage` and adds it
to the same `DbContext`, so the outbox row is written **atomically** with the aggregate change (see
`02-application-pipeline/unit-of-work.md`).

### Step 2 — the outbox processor publishes

`Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs` (extending the shared
`OutboxProcessorBase`) fetches a batch of unprocessed rows (`WHERE processed_on_utc IS NULL ORDER BY id
LIMIT 50`) via EF Core, publishes each on the in-memory bus, then marks the row processed. Because the
mark happens **after** publish, delivery is at-least-once **by design** — running two drainers would just
publish some rows twice, which the idempotent inbox absorbs. (Adding `FOR UPDATE SKIP LOCKED` to the outbox
fetch is a duplicate-reduction optimization, not a correctness fix; multi-instance *correctness* is enforced
on the consumer side — see the inbox claim below.)

### Step 3 — the notification handler calls the bus

`ProductCreatedNotificationHandler` maps the module's internal notification to the public
`ProductCreatedIntegrationEvent` and calls `IEventsBus.Publish(...)`.

**When Path A is used:** for **domain-driven** state changes where the integration event must not be lost if
the originating transaction commits.

**Reliability implications (Path A):**
- ✅ The **write** of the integration intent is durable: the outbox row is committed atomically with the
  aggregate (transactional outbox). A crash before delivery does not lose the event — the processor retries.
- ⚠️ The **delivery** step (`IEventsBus.Publish`) is in-memory. Durable end-to-end delivery still depends on
  the consumer persisting to an inbox.
- ⚠️ The outbox drain is **at-least-once**, so consumers must tolerate duplicates (dedup at the inbox).

**Safe to copy directly?** The *transactional outbox write* — yes. The *outbox → in-memory-bus delivery* is
only as reliable as the consumer's inbox.

---

## Path B — Direct `IEventsBus.Publish` (no outbox)

A command handler, application service, or background job publishes directly, with **no outbox row**.

**When Path B is used:**
- High-frequency / high-volume signals where an outbox would be a bottleneck (must be a conscious decision).
- Background/best-effort notifications.

**Reliability implications (Path B):**
- ⚠️ **No durability.** If the process crashes between the DB write and `Publish`, or if `Publish` fails, the
  event is **lost**. If the publish happens *after* committing the DB write, the DB state can be committed
  while the event never arrives.

**Safe to copy directly?** Only for **best-effort, droppable** events, and only if explicitly documented as
such. Never for events another module relies on for correctness.

---

## Consumption: inbox vs. direct-to-MediatR

A consumer can handle a received integration event in two ways:

- **Inbox writer** (the durable path, used by Notifications): on receive, ingest into
  `notifications.inbox_messages`, made idempotent by a unique index on `(logical_id, occurred_on_utc)`. The
  writer checks for an existing row and, if a concurrent delivery inserts the duplicate first, swallows the
  unique-violation (`23505`) — so re-delivery never produces a second row. A separate inbox processor then
  drains the inbox with retry + dead-letter. See `Notifications.Infrastructure/Inbox/InboxWriter.cs` and
  `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs`.
  **Idempotency contract:** the key is `(logical_id, occurred_on_utc)`, both taken from the producer's
  `IntegrationEvent`. This provides *event/transport* idempotency (not business idempotency) and assumes
  that key is **stable across redeliveries** and the payload is **immutable per key** — a differing payload
  under the same key is silently treated as a duplicate. If a producer can re-timestamp a retry (a fresh
  `occurred_on_utc`), it would create a second row and a second effect; key on a stable message id instead.
- **Direct-to-MediatR** (the risky path): publish straight to the module's mediator on receive, with no
  inbox, no retry, no dead-letter. **Do not use this for events that matter** — a swallowed handler
  exception silently loses the event.

The inbox processor is the mature piece:

```text
SELECT due row ids (status IN ('pending','retrying') AND next_retry_on_utc IS NULL/≤ now),
       ORDER BY occurred_on_utc, LIMIT 50
  → for each id, in its OWN transaction:
        → claim the row: SELECT ... WHERE id = ? AND processed_on_utc IS NULL FOR UPDATE SKIP LOCKED
              → no row (already processed, or locked by another instance): commit and move on
        → dispatch to the module's handler
        → on success: mark processed + commit (business effect and mark commit together)
        → on failure: roll back, then record retry per InboxRetryPolicy; after N attempts → dead-letter
```

The per-row claim uses `FOR UPDATE SKIP LOCKED`, so running more than one API instance never
**double-applies the effect**: a second drainer skips a row already claimed by the first instead of
double-dispatching it. See `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs`, pinned
by `InboxConcurrencyReliabilityTests`.

> **Known race (found by claim audit, fix pending).** The lock guards only the *apply* path, not the
> *failure-recording* path. `RecordFailureAsync` runs in a separate, unlocked transaction and reads the row
> without re-checking `processed_on_utc`; so if drainer A fails and rolls back while drainer B then claims
> the row and succeeds, A can overwrite B's success with a spurious `retrying`/dead-letter status, a bumped
> `retry_count`, and retried/dead-lettered metrics — possibly even a dead-letter record for a message that
> succeeded. The **local effect stays exactly-once** (the claim query filters `processed_on_utc IS NULL`),
> so this is a state/observability corruption, not double execution. Details and the planned test-first fix:
> `09-lessons-learned/inbox-stale-failure-write-race.md`.

Retry/backoff is defined in `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs`; exhausted messages move
to a dead-letter record with a resolution workflow.

---

## Ordering & timing (sequencing)

Delivery is asynchronous and at-least-once, so the kit is designed to be correct **without** assuming
global ordering or exactly-once *timing*:

- **Producer order.** The outbox drains `ORDER BY id` (insertion order), so a single module's events are
  published in the order they were committed.
- **Consumer order.** The inbox drains `ORDER BY occurred_on_utc`, so due messages are processed in
  event-time order within a batch.
- **No end-to-end ordering guarantee.** A failed message moves to `retrying` with a backoff delay
  (`InboxRetryPolicy`), so its effect can land **after** messages that arrived later. With retries and
  redeliveries, arrival order ≠ commit order in the general case.
- **Design consequence.** Handlers must be **idempotent** and, where feasible, **order-independent
  (commutative)**. The inbox's idempotent ingest absorbs duplicates/redeliveries no matter when or how
  often they arrive, and each message's effect + "processed" mark commit in a single transaction, so a
  message is never left half-applied or interleaved with another.
- **Timing is eventual, not immediate.** Effects surface only after the background drains tick (plus any
  retry backoff); consumers and read models are **eventually consistent**. The HTTP e2e test mirrors this
  by draining the processors explicitly before it asserts.

## End-to-end reliability summary

| Hop | Mechanism | Guarantee |
| --- | --------- | --------- |
| Aggregate change → outbox row (Path A) | one explicit transaction | **Atomic** (transactional outbox) |
| Outbox row → notification handler | batched EF drain, mark-after-publish | At-least-once (duplicates possible) |
| Notification handler → `IEventsBus.Publish` | in-memory bus | In-process; exceptions surface (not swallowed) |
| `IEventsBus` → subscriber (inbox writer) | unique index `(logical_id, occurred_on_utc)` + swallow `23505` | Idempotent ingest → durable |
| `IEventsBus` → subscriber (direct) | mediator publish | **Best-effort, droppable** |
| Inbox row → handler | drain + retry + dead-letter; per-row `FOR UPDATE SKIP LOCKED` claim | At-least-once with dead-letter; **exactly-once *local* apply** (effect is multi-instance safe; the *failure-recording* path has a known stale-write race — see note above; external side effects out of scope) |
| Direct publish (Path B) | `IEventsBus.Publish` only | **Best-effort, droppable, no record** |

## What to copy

- The **transactional outbox** write (Path A step 1) and the **batched mark-after-publish processor** (`OutboxProcessorBase`).
- The **inbox with idempotent ingest + retry/dead-letter** (the most mature messaging piece here).
- The **domain-event → notification → integration-event** separation.

## What not to copy blindly

- The **in-memory bus as the cross-module transport** if modules might ever run as separate processes.
  (The kit ships an opt-in NATS JetStream transport behind the same `IEventsBus` for exactly this case —
  `BuildingBlocks.Infrastructure/Events/NatsEventBus.cs`, pinned by `NatsCrossProcessReliabilityTests`.)
  Note its topology: it binds **one durable consumer per event type** (`{DurablePrefix}-{EventType}`),
  giving competing-consumer (queue-group) semantics across instances of one deployment. Independent
  consumer groups (true pub/sub fan-out across modules) would need a per-consumer durable identity and are
  **not** implemented — consumer-group identity is part of your deployment topology.
- **Path B (direct publish)** for anything other than explicitly droppable events.
- An **inconsistent consumer model** (inbox vs direct). Pick one and apply it uniformly, or classify each
  event explicitly (see reliability matrix).

## Open questions

- Is the in-memory bus a deliberate "single process forever" decision, or interim before a broker?
- Should every durable-event consumer be required to use the inbox (enforced by a test)?

## Next

- `05-events-and-messaging/reliability-matrix.md`
- `09-lessons-learned/architecture-rules-for-my-own-project.md`
