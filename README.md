# Modulith Reliability Kit

[![CI](https://github.com/qwertyboy0325/modulith-reliability-kit/actions/workflows/ci.yml/badge.svg)](https://github.com/qwertyboy0325/modulith-reliability-kit/actions/workflows/ci.yml)

**In plain terms:** a small, test-backed .NET reference for a classic backend failure mode — *the database changed, but the event that should have followed it was never delivered, and nothing recorded that it was lost.* The repo shows how to make that hand-off reliable, and recoverable when it still fails.

A .NET 8 **modular-monolith / DDD** backend that treats **cross-module messaging reliability**
as a first-class design concern.

It is deliberately small — one producer module (`Catalog`) emits one integration event and one
consumer module (`Notifications`) handles it — because it is a demonstrator of the reliability
machinery, not a system to rebuild. The thesis it works out is that *durable publish is not durable
delivery*, using a transactional outbox, an idempotent inbox, retry/dead-letter, and an opt-in NATS
JetStream transport for cross-process delivery. The design is distilled from problems hit while
operating high-throughput, multi-tenant modular monoliths in production.

Every guarantee below links to the code that enforces it and the test that pins it. The fastest way in
is the [guarantee → code → test map](#verify-the-claims-guarantee--code--test); the limits of each
guarantee, and the fault model the tests use, are in
[Guarantee boundaries & non-goals](#guarantee-boundaries--non-goals).

> **A concurrency lesson included:** `FOR UPDATE SKIP LOCKED` protected the inbox apply path, but it did
> not automatically protect a later failure-recording transaction from overwriting another worker's success.
>
> This repository preserves a deterministic red-to-green reproduction of that stale failure-write race:
> [read the case study](docs/09-lessons-learned/inbox-stale-failure-write-race.md).

## The reliability model

Each hop in a cross-module event flow has a *different* guarantee. Conflating them is how
"the database changed but nobody was notified, with no record" bugs happen.

```mermaid
flowchart LR
  A[Aggregate change] -->|one EF txn · atomic| O[(outbox)]
  O -->|drain · mark-after-publish · at-least-once| BUS{{IEventsBus · in-memory}}
  BUS -->|idempotent ingest · unique index| IN[(inbox)]
  IN -->|SKIP LOCKED claim · exactly-once local apply| FX([announcement effect])
  FX -.->|failure · retry w/ backoff| IN
  IN -.->|max attempts| DL[(dead-letter)]
  DL -.->|operator reprocess| IN
```

Each edge is a claim with a home in the code and a test that pins it — see the
[verification map](#verify-the-claims-guarantee--code--test) below. The bus is in-memory by default; an
opt-in NATS JetStream transport makes the same publish→consume path durable across processes (same
diagram, real broker).

| Hop | Mechanism | Guarantee |
| --- | --------- | --------- |
| Aggregate change → outbox row | single EF transaction | atomic (transactional outbox) |
| Outbox row → bus publish | background processor, mark-after-publish | at-least-once (consumers must be idempotent) |
| Bus → consumer (durable) | inbox ingest + retry + dead-letter + operator reprocess | at-least-once, dead-lettered, and recoverable |
| Direct publish (no outbox) | best-effort only | droppable — allowed *only* if explicitly classified |

Full analysis and the per-event classification method:
[`docs/05-events-and-messaging/reliability-matrix.md`](docs/05-events-and-messaging/reliability-matrix.md)
and [`integration-events.md`](docs/05-events-and-messaging/integration-events.md).

## Verify the claims (guarantee → code → test)

Each claim has a specific place it is enforced and a test that pins it.

| Guarantee | Enforced in | Pinned by test |
| --------- | ----------- | -------------- |
| Aggregate change + outbox row commit **atomically** (one transaction) | [`UnitOfWorkBehavior.cs`](src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/Pipeline/UnitOfWorkBehavior.cs) | [`CatalogProductWriteReliabilityTests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Catalog/CatalogProductWriteReliabilityTests.cs) · `Creating_A_Product_Commits_The_Aggregate_And_Outbox_Row_Together` |
| Outbox publish is **at-least-once**: a committed-but-unpublished row is published on the next drain and marked processed; a processed row is not re-published. (A crash *after* publish but *before* the mark yields a duplicate publish **by design**, absorbed by the inbox.) | [`CatalogOutboxProcessor.cs`](src/Modules/Catalog/ModulithReliabilityKit.Modules.Catalog.Infrastructure/Processing/CatalogOutboxProcessor.cs) · [`OutboxProcessorBase.cs`](src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/Processing/OutboxProcessorBase.cs) | [`CatalogOutboxReliabilityTests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Catalog/CatalogOutboxReliabilityTests.cs) · `Committed_Unpublished_Outbox_Row_Is_Published_Then_Marked` · duplicate tolerance: [`CrossModuleReliabilityE2ETests`](src/Tests/ModulithReliabilityKit.IntegrationTests/CrossModule/CrossModuleReliabilityE2ETests.cs) · `Outbox_Redelivery_Is_Absorbed_Idempotently_By_The_Inbox` |
| Inbox ingest is **idempotent** (duplicate delivery ⇒ one row, one effect) | [`InboxWriter.cs`](src/Modules/Notifications/ModulithReliabilityKit.Modules.Notifications.Infrastructure/Inbox/InboxWriter.cs) | [`NotificationsInboxReliabilityTests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Notifications/NotificationsInboxReliabilityTests.cs) · `Duplicate_Delivery_Produces_Exactly_One_Inbox_Row_And_One_Effect` |
| Inbox apply commits the **local DB effect + `processed` mark in one transaction (exactly-once *local* apply)**; a failed apply rolls back and recovers. *External* side effects are out of scope — the shipped dispatcher only writes to the same database. | [`NotificationsInboxProcessor.cs`](src/Modules/Notifications/ModulithReliabilityKit.Modules.Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs) | `NotificationsInboxReliabilityTests` · `Crash_After_Staging_Effect_Rolls_Back_And_Recovers_Exactly_Once` (failure injected as a handler throw → rollback) |
| Inbox apply is **multi-instance safe**: concurrent drainers claim rows with `FOR UPDATE SKIP LOCKED` (local effect applied exactly once, never double-applied), and the post-rollback **failure-recording** path re-claims the row under a blocking `FOR UPDATE` and no-ops if another drainer already processed it — so a losing drainer cannot overwrite the success with a spurious `retrying`/dead-letter status or metric (a race found by the claim audit and fixed test-first; see [inbox stale-failure write race](docs/09-lessons-learned/inbox-stale-failure-write-race.md)). *Multi-instance safety is consumer-side; the outbox has no publisher claim — see the boundaries below.* | [`NotificationsInboxProcessor.cs`](src/Modules/Notifications/ModulithReliabilityKit.Modules.Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs) | [`InboxConcurrencyReliabilityTests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Notifications/InboxConcurrencyReliabilityTests.cs) · `A_Row_Claimed_By_One_Processor_Is_Skipped_By_A_Concurrent_Processor` · `Failure_Recording_After_A_Concurrent_Success_Does_Not_Overwrite_The_Processed_Row` |
| **Retry** with back-off, then **dead-letter** after max attempts | [`InboxRetryPolicy.cs`](src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Application/Inbox/InboxRetryPolicy.cs) | [`InboxRetryPolicyTests`](src/Tests/ModulithReliabilityKit.ReliabilityTests/Inbox/InboxRetryPolicyTests.cs) (unit) · `NotificationsInboxReliabilityTests.Repeated_Failures_Dead_Letter_The_Message_After_Max_Attempts` |
| **Dead-letter recovery**: a poisoned message can be requeued and its local effect applied **exactly once**; requeue + resolution commit atomically; sequential *and* concurrent re-runs are no-ops (serialized by a `FOR UPDATE` claim) | [`InboxDeadLetterReprocessor.cs`](src/Modules/Notifications/ModulithReliabilityKit.Modules.Notifications.Infrastructure/Inbox/InboxDeadLetterReprocessor.cs) | [`InboxDeadLetterReprocessTests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Notifications/InboxDeadLetterReprocessTests.cs) · `Reprocessing_A_Dead_Letter_Requeues_It_And_Applies_The_Effect_Exactly_Once` · `Concurrent_Reprocess_Of_The_Same_Dead_Letter_Requeues_Exactly_Once` |
| End-to-end: create ⇒ durable consume ⇒ **exactly one** announcement; redelivery absorbed idempotently | wiring in [`Program.cs`](src/Api/ModulithReliabilityKit.Api/Program.cs) | [`CrossModuleReliabilityE2ETests`](src/Tests/ModulithReliabilityKit.IntegrationTests/CrossModule/CrossModuleReliabilityE2ETests.cs) (2 tests) |
| Same story through the **HTTP surface** (create via API ⇒ read announcement via API) | API endpoints in `Program.cs` | [`CatalogToNotificationsHttpE2ETests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Http/CatalogToNotificationsHttpE2ETests.cs) |
| Cross-module references allowed **only** via `IntegrationEvents`; layer direction | project boundaries | [`ArchitectureTests`](src/Tests/ModulithReliabilityKit.ArchitectureTests) |
| **Durable, cross-process transport** (opt-in): `Publish` awaits a JetStream `PubAck` (the broker accepted the message into the stream under its configured retention/storage) before the outbox marks the row; delivery to a durable consumer is at-least-once across processes. *Broker replication, retention sizing, storage exhaustion, and stream/consumer lifecycle are out of scope.* | [`NatsEventBus.cs`](src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/Events/NatsEventBus.cs) (NATS JetStream) | [`NatsCrossProcessReliabilityTests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Messaging/NatsCrossProcessReliabilityTests.cs) (publish-before-subscriber delivered once a subscriber starts; failed handler → redelivery) |
| **Observability**: inbox outcomes (processed / retried / dead-lettered) are emitted as metrics from the real drain path, scrapeable at `/metrics`; spans are **per-process** (no cross-process trace propagation) | [`ReliabilityMetrics.cs`](src/BuildingBlocks/ModulithReliabilityKit.BuildingBlocks.Infrastructure/Diagnostics/ReliabilityMetrics.cs) | [`ReliabilityMetricsInstrumentationTests`](src/Tests/ModulithReliabilityKit.IntegrationTests/Notifications/ReliabilityMetricsInstrumentationTests.cs) |

The center of the design is
[`NotificationsInboxProcessor.cs`](src/Modules/Notifications/ModulithReliabilityKit.Modules.Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs)
(exactly-once local apply + retry + dead-letter in one transaction); its end-to-end behaviour is
exercised by
[`CrossModuleReliabilityE2ETests.cs`](src/Tests/ModulithReliabilityKit.IntegrationTests/CrossModule/CrossModuleReliabilityE2ETests.cs).

## Use this when

- A relational database update must trigger downstream asynchronous work.
- You need to reason about a DB commit that succeeds before a message is published.
- Your consumers can receive duplicate or redelivered messages.
- You use PostgreSQL-backed workers, inbox tables, or `FOR UPDATE SKIP LOCKED`.
- You want each reliability claim tied to implementation code and an integration test.

## Do not use this when

- You need a drop-in production messaging framework.
- You need end-to-end exactly-once behavior across HTTP, email, payments, or webhooks.
- You need broker HA, multi-region failover, stream lifecycle management, or throughput benchmarking guidance.
- You need a Kafka implementation; this repository uses NATS JetStream as one durable transport example.

## Guarantee boundaries & non-goals

This kit is deliberate about *what it proves* and *under which fault model*. The claims above are pinned
by integration tests against a real PostgreSQL and a real NATS server at a **single-node, ≤2-instance**
topology; they are not a proof of production operation at scale.

**What is guaranteed (and tested):**

- **Transactional outbox write** — the aggregate change and the outbox row commit in one transaction.
- **At-least-once publish** — the outbox is `publish → mark`; a crash between them re-publishes (a
  duplicate by design), absorbed downstream. There is **no outbox lease/claim**, so multiple instances
  may publish the same row; correctness relies on the idempotent inbox, not on deduplicated publishing.
- **Idempotent inbox ingest** — dedup on `(logical_id, occurred_on_utc)`. This assumes that key is
  **stable across redeliveries** and the payload is **immutable per key** (it is *event/transport*
  idempotency, not business idempotency). A differing payload under the same key is treated as a
  duplicate.
- **Exactly-once *local database* apply** — the inbox dispatcher effect and the `processed` mark commit
  in one transaction, so the local effect lands once under duplicate delivery and concurrent drainers
  (`FOR UPDATE SKIP LOCKED`). **External side effects (HTTP / webhook / email / payment) are not
  covered** — the shipped dispatcher only writes to the same database. The post-failure bookkeeping
  (`RecordFailureAsync`) also re-claims the row under a blocking `FOR UPDATE` and no-ops if a concurrent
  drainer already processed it, so a losing drainer cannot record a spurious retry/dead-letter over the
  success (see [inbox stale-failure write race](docs/09-lessons-learned/inbox-stale-failure-write-race.md)).
- **Bounded retry → dead-letter → operator reprocess** on the inbox. Reprocess is idempotent **and
  concurrency-safe**: two operators reprocessing the same dead-letter serialize on a `FOR UPDATE` row
  claim, so it is requeued exactly once (the second request observes it already resolved).
- **Durable cross-process transport (opt-in, NATS JetStream)** — `Publish` awaits a `PubAck` (the broker
  accepted the message into a stream under its configured retention/storage); delivery to a durable
  consumer is at-least-once, and lost acks cause redelivery, collapsed to one local effect by the inbox.

**Fault model of the tests.** Failures are injected as **transaction rollbacks** (a throwing handler)
and **pre-publish states** (a committed-but-unpublished row), plus real NATS redelivery. They do **not**
include OS-level process kills, connection loss mid-commit, or broker failure.

**Explicit non-goals (operator / adopter responsibility):**

- Outbox retry backoff, outbox dead-letter, and poison-message quarantine — a permanently
  unserializable outbox row is retried every polling interval indefinitely.
- Global / end-to-end message ordering; backpressure, backlog, and throughput control; JetStream
  ack-deadline tuning.
- Broker replication, retention sizing, storage-exhaustion handling, stream/consumer lifecycle,
  broker-side dedup (`Nats-Msg-Id`), and multi-region failover.
- Generic cross-module pub/sub fan-out: the NATS transport binds **one durable consumer per event type**
  (competing-consumer semantics across instances of one deployment), not independent consumer groups.
- Cross-process (distributed) trace-context propagation — only per-process spans are emitted.

## Implementation status

Stated so the code and the claims match:

| Capability | Status |
| ---------- | ------ |
| BuildingBlocks (Domain / Application / Infrastructure), MediatR-free domain | Implemented |
| Explicit-transaction Unit of Work, decorator pipeline (validation → UoW → logging) | Implemented |
| Two reference modules (`Catalog` producer, `Notifications` consumer) with module facades + typed module persistence | Implemented |
| Transactional **outbox** (publish path) + PostgreSQL migrations | Implemented |
| Durable **inbox** consumer (idempotent ingest, retry, dead-letter) wired end-to-end | Implemented |
| **Dead-letter recovery**: list + reprocess (requeue, apply-once) via Notifications facade & admin endpoints | Implemented |
| **Multi-instance safe** inbox drain (`FOR UPDATE SKIP LOCKED` row claim) proven by a concurrent-processor test | Implemented |
| Opt-in **durable cross-process transport** (NATS JetStream) behind the existing `IEventsBus`; in-memory remains the default | Implemented |
| **Observability**: reliability metrics (publish/processed/retried/dead-lettered/reprocessed/redelivered) on Prometheus `/metrics` + spans; traces via opt-in OTLP | Implemented |
| End-to-end durable consume across a **second module** | Implemented |
| Reliability **integration tests on real PostgreSQL** (Testcontainers): idempotency, crash recovery, dead-letter, cross-module e2e | Implemented |
| **HTTP-level e2e** (WebApplicationFactory): create product via API → durable consume → announcement readable via API | Implemented |
| Architecture tests enforcing module isolation + layering | Implemented |
| CI pipeline (build + arch/unit/integration tests on every push & PR) | Implemented |

## Architecture at a glance

```text
API host (single container, decorator pipeline, arch tests)
        │ composes
        ▼
Modules/<Context>/ {Domain, Application, Infrastructure, IntegrationEvents}
        │ cross-module references allowed ONLY via IntegrationEvents
        ▼
BuildingBlocks/ {Domain, Application, Infrastructure}
```

Only each module's `IntegrationEvents` project is a public cross-module contract — enforced by
[`src/Tests/ModulithReliabilityKit.ArchitectureTests`](src/Tests/ModulithReliabilityKit.ArchitectureTests).

## Run it

[`DEMO.md`](DEMO.md) is a runnable walkthrough: the live durable path, the failure-path guarantees
exercised by tests, and a two-instance run showing each message committed exactly once across two API
processes on one NATS + one PostgreSQL (at-least-once delivery via JetStream, collapsed by the
idempotent inbox + `FOR UPDATE SKIP LOCKED`). Reliability metrics are scrapeable at `GET /metrics`
(Prometheus); see [`docs/08-operational-concerns/observability.md`](docs/08-operational-concerns/observability.md).

```bash
dotnet build src/ModulithReliabilityKit.sln
dotnet test src/ModulithReliabilityKit.sln   # integration tests spin up throwaway PostgreSQL via Testcontainers (Docker required)
docker compose -f docker-compose.postgres.yml up -d
dotnet run --project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj --urls http://localhost:5099
```

The architecture and reliability-policy unit tests run without Docker; the `IntegrationTests` project
requires a running Docker daemon (it starts its own `postgres:16-alpine` containers and applies the
module migrations).

Create and read a sample product:

```bash
curl -X POST "http://localhost:5099/catalog/products/" \
  -H "Content-Type: application/json" \
  -d '{"name":"Demo Product","price":12.50,"currency":"usd"}'

curl "http://localhost:5099/catalog/products/{id}"
```

PostgreSQL demo runbook:
[`docs/10-skeleton/applied-improvements-and-postgres-demo.md`](docs/10-skeleton/applied-improvements-and-postgres-demo.md).

## Documentation map

Read in order; the notes are written bottom-up (foundations first).

- [`docs/README.md`](docs/README.md) — how the case study is structured and how to read it.
- `docs/00-orientation/` — project shape and a reading order.
- `docs/01-foundation/` — building blocks and dependency injection.
- `docs/02-application-pipeline/` — unit of work.
- `docs/05-events-and-messaging/` — **integration events + reliability matrix** (the core of the design).
- `docs/07-module-architecture/` — module boundaries and the DDD-reference comparison.
- `docs/09-lessons-learned/` — the extracted architecture rule set.
- `docs/10-skeleton/` — what was actually built and why.
- `docs-tw/` — Traditional Chinese mirror of the same notes.

## Reference material and attribution

This kit was built by studying modular-monolith / DDD reference codebases **and** by distilling
problems hit while operating a real production system.

**Primary architectural reference — Kamil Grzybek's
[Modular Monolith with DDD](https://github.com/kgrzybek/modular-monolith-with-ddd)** (MIT,
© 2019 Kamil Grzybek), the widely-cited open-source reference for modular monoliths in .NET, and the
single biggest influence on this kit. The module layout
(`Domain` / `Application` / `Infrastructure` / `IntegrationEvents`), the internal-command / outbox /
inbox vocabulary, and the "boundaries enforced by architecture tests" discipline follow its lead.
Where this kit deliberately diverges — e.g. a transaction-safe inbox processor instead of a shared
process-then-mark base — the notes say so, out of respect for the original rather than to claim
improvement. (Personal thanks in [Acknowledgements](#acknowledgements).)

**Informed by real-world problems (de-identified).** These distil failure modes the author hit while
operating high-throughput, multi-tenant modular monoliths in production. The examples below are
generalized on purpose: no employer, product, dataset, or proprietary identifier is referenced. What
is shared is the *engineering shape* of the problem and the design decision this kit makes in response.

- **"The database changed but the effect silently didn't happen."** Under load, asynchronous
  cross-module event processing can stall when a shared resource is exhausted — e.g. a connection
  pool starved because a compatibility/legacy adapter leaks pooled connections through
  non-deterministic disposal, which then cascades into background jobs. *Design decision in this
  kit:* background/async consumption runs in **bounded, explicitly-scoped units of work with
  deterministic resource lifetime** (scoped integration-event handlers + explicit-transaction UoW +
  a dedicated durable inbox processor), never ad-hoc data access inside a handler.
- **Silent multi-tenant isolation failure.** Defense-in-depth tenant isolation (ORM query filters +
  database row-level security) can behave unexpectedly when a connection is opened outside the path
  that sets the required tenant-scoping session state — returning empty results and blocking
  legitimate work instead of only unauthorized access. *Design decision in this kit:* each module
  owns its `DbContext` and persistence wiring is deliberate and per-module, never implicit.
- **Write amplification on a high-throughput ingest path.** At hundreds of millions of writes/day, an
  unconditional upsert of "latest state" on every packet turned into an ~100% update ratio and dead
  tuples past ~80% of the table (MVCC marks each `UPDATE`'s old version dead), degrading the whole
  store faster than autovacuum could recover it. *Design lesson (write-path sibling of the kit's
  idempotency):* make writes **conditional** (only when something materially changed), keep hot
  mutable state off the durable per-event path (write-behind on a bounded interval), and compress /
  retain history by policy with **lock-aware, non-blocking** maintenance. Full de-identified write-up:
  [`docs/09-lessons-learned/high-write-time-series-ingest.md`](docs/09-lessons-learned/high-write-time-series-ingest.md).

The distilled rule set lives in
[`docs/09-lessons-learned/architecture-rules-for-my-own-project.md`](docs/09-lessons-learned/architecture-rules-for-my-own-project.md).
The case-study notes are written against this repository's own neutral domain (Catalog /
Notifications) and carry no proprietary identifiers. Any reference codebases studied while building
this kit are kept locally under `ref/` and are **intentionally git-ignored** — study material only,
neither part of nor reproduced in this repository.

If `ref/` was ever tracked, remove it from the index once:

```bash
git rm -r --cached ref
```

## Scope, provenance, and AI-assisted authorship

To set clear expectations for anyone evaluating this repository:

- **What this is.** A purpose-built reference implementation plus a written case study on cross-module
  messaging reliability. It is *not* a copy, fork, or export of any production system.
- **No proprietary material.** It contains no source code, data, schemas, credentials, employer or
  product names, or tenant information from the systems or third-party codebases that informed it.
  The real-world problems described above are **generalized and de-identified** — real names,
  identifiers, connection details, and business specifics are removed; only the vendor-neutral
  engineering problem/solution shape is retained, and nothing here is traceable to a specific private
  system.
- **Study material stays out of the repo.** The reference codebases under `ref/` (including the public
  reference project) are kept locally for study only and are **git-ignored** — they are not
  redistributed here.
- **AI-assisted, author-directed.** The code, the documentation, and this case study were refined with
  substantial AI assistance — drafting, refactoring, distillation, and de-identification. The
  architectural judgment, the reliability thesis, the design trade-offs, and the production experience
  behind them are the author's; the AI was a tool for turning that into a clean, tested, and verifiable
  artifact. The **named** claims here are backed by the linked code and focused tests under a documented
  fault model (see [Guarantee boundaries & non-goals](#guarantee-boundaries--non-goals)), so they can be
  checked directly rather than taken on trust.

## Renaming / reuse

The repository code namespace is `ModulithReliabilityKit`. A helper script can rebrand the skeleton to
a different product/module name if you want to reuse it as a starting point — see
[`TEMPLATE.md`](TEMPLATE.md). Reuse is a secondary use case; the primary purpose of this repo is the
reliability reference implementation and the case study above.

## License

Released under the [MIT License](LICENSE). You may use, copy, modify, and redistribute the code and
documentation under its terms; copies or substantial portions must retain the copyright and license
notice. The de-identified case-study material is original writing and carries no proprietary content; any
reference codebases studied while building this kit are git-ignored and not redistributed here (see
[Reference material and attribution](#reference-material-and-attribution)).

## Acknowledgements

My heartfelt thanks to **Kamil Grzybek** and his
[**Modular Monolith with DDD**](https://github.com/kgrzybek/modular-monolith-with-ddd) (MIT,
© 2019 Kamil Grzybek). This project was my entry point into thinking seriously about software
architecture — where modular boundaries, the outbox/inbox model, and test-enforced discipline first
clicked for me. What I learned from it gave me the foundation to go on and **lead a real project**, and
to run head-first into the messy, real-world reliability problems that this kit distills and answers.
The shape of my architectural thinking owes a great deal to that work. If you find this kit useful, read
the original first: it remains the more complete and authoritative treatment, and this repository is
best understood as a focused study built in its tradition — not a replacement for it.
