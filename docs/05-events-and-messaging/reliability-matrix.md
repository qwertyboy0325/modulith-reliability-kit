# Integration Event Reliability Matrix

## Purpose

Classify each integration event by publisher, trigger path, consumers, and reliability properties. This is
the single most important artifact for deciding what is safe to ship. The matrix is a **discipline**: every
integration event gets a row before it merges. Where evidence is missing, the cell is marked **Unknown**
with a note on what is needed to resolve it.

## Areas inspected

- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs`
- `Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs`
- `Notifications.Infrastructure/Inbox/InboxWriter.cs`, `Processing/NotificationsInboxProcessor.cs`
- `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs`

## How to read the columns

- **Trigger Path:** **A** = domain event → outbox → outbox processor → notification handler → bus (durable write).
  **B** = direct `IEventsBus.Publish` (no outbox, not durable).
- **Durable?** = is the publish intent persisted before delivery (outbox)? (Not whether the bus is durable — it never is.)
- **Can Drop?** = can the event be lost end-to-end under a crash or swallowed exception?
- **Retry?** = is there automatic retry on the consumer side?
- **Inbox?** = does the consumer persist to an inbox (idempotent + retry/dead-letter) or go direct-to-MediatR?
- **Risk** = net assessment.

> Transport caveat applying to **every** row: `IEventsBus` is in-memory **by default**. A durable end-to-end
> path therefore depends on the **publish-side outbox** *and* a **consumer-side inbox**. "Durable" refers to
> the publish-side outbox write; delivery still needs an idempotent, retrying consumer to be durable
> end-to-end. An **opt-in NATS JetStream transport** (`NatsEventBus`) is available behind the same
> `IEventsBus` for cross-process durability — see `01-foundation/building-blocks.md` and
> `NatsCrossProcessReliabilityTests`.

## Matrix

This kit ships one fully-modeled event today. The remaining rows are **illustrative patterns** (using
generic "Module A / Module B" placeholders) that show the risk classes a real matrix must capture.

| Event | Publisher | Trigger Path | Consumers | Durable? (outbox) | Can Drop? | Retry? | Inbox? | Risk |
| ----- | --------- | ------------ | --------- | ----------------- | --------- | ------ | ------ | ---- |
| `ProductCreatedIntegrationEvent` | Catalog (`ProductCreatedNotificationHandler`) | A | Notifications | Yes | At final hop only | Yes (Notifications inbox) | Yes (Notifications) | **Low** — the reference durable path in this kit |
| *(illustrative)* Module A "entity registered" | Module A | A | Module B (direct-to-MediatR) | Yes | Yes (consumer swallows errors) | No | No (direct) | **High** — durable publish thrown away at last hop |
| *(illustrative)* Module A "setting changed" | Module A (direct publish) | **B** | Module B | **No** | **Yes** | No | No (direct) | **High** — direct publish + direct consume; lost on crash |
| *(illustrative)* high-volume signal | Module A (background) | **B** | Module B | **No** | **Yes (by design)** | No | No (fast path) | **Accepted** — intentionally best-effort, must be documented |
| *(illustrative)* published-with-no-subscriber | Module A | A | none found | Yes | Yes (no subscriber → silent drop) | No | No | **Medium** — durable but possibly unconsumed / dead weight |

### Consumer subscription map (this kit)

| Consumer module | Subscribes to | Consumer style |
| --------------- | ------------- | -------------- |
| Notifications | `ProductCreatedIntegrationEvent` | Inbox (durable, idempotent, retry + dead-letter) |
| Catalog | none (publisher only) | — |

## Reading the matrix: the core findings (lessons)

1. **Durable publish ≠ durable delivery.** An event written durably to the outbox (Path A) but consumed by a
   **direct-to-MediatR** handler that swallows exceptions throws the durability away at the last hop. The
   consumer must use an inbox to keep the guarantee.
2. **Path B events are simply droppable.** A direct publish has no outbox; if it fires *after* committing the
   DB write, the system can reach a state where the DB changed but no one was notified, with no record.
3. **The fully durable pattern** is outbox publish → inbox consume with retry/dead-letter. In this kit that
   is `ProductCreatedIntegrationEvent` (Catalog → Notifications). It is the reference; anything weaker must be
   a conscious choice.
4. **`Unknown` rows are real gaps.** A published event with no found subscriber is either dead weight or a
   subscription that lives somewhere not yet inspected — resolve it before shipping.
5. **Dead-letter is a holding state, not a grave.** A message that exhausts its retries is parked, not lost —
   the payload and last error are preserved. Once the downstream cause is fixed, an operator requeues it and
   the normal (idempotent) inbox drain applies its **local effect exactly once**; the requeue and the
   dead-letter resolution are staged on one `DbContext` and committed by a single `SaveChanges`, so a
   message is never simultaneously dead-lettered and pending. Recovery loop:
   `InboxDeadLetterReprocessor` + `POST /notifications/inbox/dead-letters/{id}/reprocess`, pinned by
   `InboxDeadLetterReprocessTests`. **Scope:** reprocess is proven idempotent for *sequential* re-runs
   (already-resolved → no-op); *concurrent* operator reprocess of the same dead-letter is not currently
   guarded — the final effect still lands once via the inbox claim, but the resolution bookkeeping can race.

## What evidence is missing (to resolve `Unknown`)

- A full enumeration of every publisher's integration events and every consumer's subscriptions, to build a
  definitive publisher→consumer adjacency list.
- Confirmation of which consumers are inbox vs. direct for *each* subscribed type.
- Whether any best-effort (Path B) event is treated as correctness-critical (which would make "Can Drop = Yes"
  a real bug).

## What to copy

- The classification discipline itself: **every integration event must have a row in a matrix like this**
  before it ships.
- The one fully-durable pattern: outbox publish + inbox consume + retry/dead-letter.
- A **recovery path out of the dead-letter table** (requeue → idempotent re-apply), so operators can drain a
  poison-message backlog after a fix instead of hand-editing rows.

## What not to copy blindly

- Any **High**-risk row. In particular, do not copy direct-publish-after-commit, or
  direct-consume-with-swallowed-errors, for events that matter.

## Open questions

- Which events are correctness-critical vs. genuinely best-effort? (Requires domain input.)
- Should every durable-event consumer be required to use the inbox, enforced by an arch/reliability test?

## Next

- `09-lessons-learned/architecture-rules-for-my-own-project.md`
