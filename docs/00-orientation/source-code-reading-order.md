# Source Code Reading Order

## Purpose

A bottom-up reading path for a new engineer who wants to *understand the architecture*, not the
business domain. Follow the steps in order. Each step states **why**, the **key files/areas**, and the
**questions to answer while reading**.

Do not start from endpoints or business features. Start from the shared kernel and the composition root.

## Scope

This document indexes areas detailed in later docs. The canonical producer module used throughout is
**Catalog** (aggregate `Product`); the canonical consumer is **Notifications**.

---

## Step 1 — BuildingBlocks (the shared kernel)

**Why:** every module is built on these abstractions. If you don't understand them, nothing else makes sense.

**Key areas:**
- `BuildingBlocks.Domain/` — the `Entity` / `ValueObject` / `IAggregateRoot` base types, domain-event and
  business-rule abstractions, and the strongly-typed-ID base.
- `BuildingBlocks.Application/` — the event contracts (`IEventsBus`, `IntegrationEvent`), domain-event
  notifications, the outbox contract (`IOutbox`/`OutboxMessage`), and the inbox contracts including
  `Inbox/InboxRetryPolicy.cs`.
- `BuildingBlocks.Infrastructure/` — the unit-of-work behavior and domain-event dispatching.

**Questions to answer:**
- How does an aggregate record a domain event? (via the `Entity` base)
- How is a domain event mediated in-process, and how does it relate to the notification abstraction?
- What is the difference between a **domain event**, a **domain-event notification**, and an **integration event**?
- Where is the **consistency boundary** declared? (Hint: see Step 5.)

---

## Step 2 — Composition root

**Why:** understanding how modules are wired early prevents confusion later. This kit uses a **single
MS.DI container**; each module contributes registrations through an `Add<Module>Module(...)` extension.

**Key files:**
- `src/Api/ModulithReliabilityKit.Api/Program.cs` — the host and DI composition.
- `src/Api/ModulithReliabilityKit.Api/Modules/CatalogEndpoints.cs` — HTTP → command/query mapping.

**Questions to answer:**
- Who registers each module, and when? (`Add<Module>Module` from `Program.cs`)
- What is shared from the host into each module? (the event bus, connection configuration)
- How is a command executed? (endpoint → module facade → MediatR `Send` inside the module)

---

## Step 3 — Module registration & the command pipeline

**Why:** the registrations define lifetimes, pipeline behaviors, and typed persistence. This is where
validation / unit-of-work / logging wrapping is configured.

**Key areas:**
- Each module's `Add<Module>Module` extension (in `<Module>.Infrastructure`).
- The pipeline behaviors in `BuildingBlocks.Infrastructure`, especially
  `Pipeline/UnitOfWorkBehavior.cs`.

**Questions to answer:**
- What lifetime does `DbContext` have, and how is it scoped per request?
- In what **order** are pipeline behaviors applied? (Validation → UnitOfWork → Logging — verify by reading registration order.)
- How are MediatR handlers discovered?

Full detail: `01-foundation/dependency-injection.md`.

---

## Step 4 — Command pipeline

**Why:** all state changes flow through command handlers wrapped by pipeline behaviors. This is the
application's transaction story.

**Key files:**
- `BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`
- A real handler: `Catalog.Application/Products/.../CreateProductCommandHandler`

**Questions to answer:**
- Does the handler call `SaveChanges`? (No — the unit-of-work behavior does.)
- What happens if validation fails? (an invalid-command exception before the handler runs)
- Where does the domain-event dispatch happen relative to `SaveChanges`?

---

## Step 5 — Unit of Work

**Why:** this defines the consistency boundary. It is small and worth reading carefully.

**Key files:**
- `BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs`
- The domain-events dispatcher in `BuildingBlocks.Infrastructure`.

**Questions to answer:**
- Does commit open an explicit DB transaction? (Yes — the kit makes the transaction explicit.)
- Are domain state changes and outbox rows persisted **atomically**? (Yes — same transaction.)

Full detail: `02-application-pipeline/unit-of-work.md`.

---

## Step 6 — Domain events → outbox

**Why:** this is the bridge from in-process domain events to durable, cross-module messaging.

**Key files:**
- The domain-events dispatcher in `BuildingBlocks.Infrastructure`.
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs` (enqueues the integration event into the outbox).
- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs` (the public contract).

**Questions to answer:**
- How does a domain event become an outbox row? (the dispatcher serializes the *notification*)
- What maps a notification type to a stored `type` string? (explicit notification mapping)
- Is the outbox row written in the same `SaveChanges` as the aggregate change?

---

## Step 7 — Outbox / Inbox processing

**Why:** the durable delivery mechanics, retries, and dead-lettering live here.

**Key files:**
- `BuildingBlocks.Infrastructure/Processing/OutboxProcessorBase.cs` (shared processor base)
- `Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs` (producer-side outbox drain)
- `Notifications.Infrastructure/Inbox/InboxWriter.cs` (idempotent inbox ingest)
- `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs` (inbox drain with retry/dead-letter)
- `BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs` (retry/backoff schedule)

**Questions to answer:**
- How does the outbox processor drain rows? (batched EF fetch of `processed_on_utc IS NULL`, publish, then per-row mark-after-publish → at-least-once)
- How does the consumer make ingest idempotent? (unique index on `(logical_id, occurred_on_utc)` + swallow `23505` in the inbox writer)
- What is the retry/dead-letter policy? (see `InboxRetryPolicy`)

Full detail: `05-events-and-messaging/integration-events.md`, `reliability-matrix.md`.

---

## Step 8 — Integration events (the bus)

**Why:** the actual cross-module transport. Critically, it is **in-memory only**.

**Key areas:**
- The `IEventsBus` contract in `BuildingBlocks.Application`.
- The in-memory event bus in `BuildingBlocks.Infrastructure`.

**Questions to answer:**
- Is the bus durable across process restarts? (No — in-memory.)
- What happens to a published event with no subscribers?
- Are publish exceptions propagated or swallowed? (In this kit, the default is to **not** swallow — durability still comes from outbox/inbox, not the bus.)

---

## Step 9 — One concrete flow end-to-end (Catalog → Notifications)

**Why:** now assemble the whole picture across a producer and a consumer.

**Key files:**
- `Catalog.Domain/Products/Product.cs` + its repository interface
- `Catalog.Application/Products/CreateProduct/` (command, handler, validator)
- `Catalog.Application/Products/Events/ProductCreatedNotificationHandler.cs`
- `Catalog.IntegrationEvents/ProductCreatedIntegrationEvent.cs`
- `Notifications.Infrastructure/Inbox/InboxWriter.cs` + `Processing/NotificationsInboxProcessor.cs`

**Questions to answer:**
- Trace one command: HTTP → `ICatalogModule.ExecuteCommandAsync` → pipeline → handler → repository → UoW →
  domain event → outbox → outbox processor → `IEventsBus.Publish` → Notifications inbox → inbox processor → read model.
- Where exactly could a message be lost in that chain? (Cross-reference the reliability matrix.)

## Next

- `01-foundation/building-blocks.md`
