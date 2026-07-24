# Handoff Semantics

[![CI](https://github.com/qwertyboy0325/handoff-semantics/actions/workflows/ci.yml/badge.svg)](https://github.com/qwertyboy0325/handoff-semantics/actions/workflows/ci.yml)

A test-backed reference for reliability semantics at the database-to-message boundary.

> **Not a drop-in messaging framework or a Dapr replacement.**
> This repository deliberately keeps the application model small so that the reliability contracts around a database-to-message handoff remain inspectable and testable: event identity, idempotent local effects, concurrent worker state transitions, retry/dead-letter behaviour, and the boundary between a local transaction and external delivery.
>
> A production system may reasonably use Dapr, a broker SDK, or another framework. Those choices do not remove the need to define and verify these application-level contracts.

> **C# / .NET reviewer（繁體中文）：** [8 分鐘看懂這個專案：DDD、EF Core、MediatR 與 Event-Driven 流程](docs-tw/00-orientation/csharp-project-tour.md)

## What this repository demonstrates

One producer module (`Catalog`) emits one integration event and one consumer module (`Notifications`) handles it. The narrow domain is intentional: this is a reliability reference and case study, not a system to rebuild or a generic toolkit to adopt wholesale.

The example works through a specific thesis: **durable publish is not durable delivery.** It uses a transactional outbox, an idempotent inbox, retry/dead-letter handling, and an opt-in NATS JetStream transport to make the resulting guarantees and limitations explicit.

Every stated guarantee is linked to implementation code and focused tests. Start with the [reliability matrix](docs/05-events-and-messaging/reliability-matrix.md), the [integration-event model](docs/05-events-and-messaging/integration-events.md), or the [guarantee boundaries](#guarantee-boundaries--non-goals).

> **Concurrency case study:** `FOR UPDATE SKIP LOCKED` protected the inbox-apply path, but did not automatically protect a later failure-recording transaction from overwriting another worker's success.
>
> The repository preserves a deterministic red-to-green reproduction of that stale failure-write race: [read the case study](docs/09-lessons-learned/inbox-stale-failure-write-race.md).

## The reliability model

Each hop in a cross-module event flow has a different guarantee. Conflating them is how “the database changed but nobody was notified, with no record” failures happen.

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

| Hop | Mechanism | Guarantee |
| --- | --- | --- |
| Aggregate change → outbox row | one EF transaction | atomic transactional outbox write |
| Outbox row → bus publish | background processor; mark after publish | at-least-once; consumers must tolerate duplicates |
| Bus → consumer | idempotent inbox ingest; retry; dead-letter; operator reprocess | at-least-once, recoverable local processing |
| Inbox apply → local effect | one transaction for the local effect and processed mark | exactly-once *local database* apply |
| Direct publish without an outbox | best effort only | intentionally droppable, only when explicitly classified |

## What is verified

The tests pin the following claims under the documented fault model:

- Aggregate change and outbox row commit atomically.
- A committed-but-unpublished outbox row is published on a later drain; duplicate publish is absorbed by the idempotent inbox.
- Duplicate delivery creates one inbox row and one local effect.
- Local database effect and inbox `processed` state commit together.
- Concurrent inbox drainers use `FOR UPDATE SKIP LOCKED`; a losing failure-recording transaction cannot overwrite a concurrent success.
- Failed work retries with backoff, dead-letters after bounded attempts, and can be safely reprocessed.
- An opt-in NATS JetStream transport demonstrates durable, cross-process at-least-once delivery.

The implementation and tests behind those claims are mapped in [the reliability matrix](docs/05-events-and-messaging/reliability-matrix.md) and the repository test suites.

## Use this when

- A relational database update must trigger downstream asynchronous work.
- You need to reason about a database commit that succeeds before a message is published.
- Consumers may receive duplicate or redelivered events.
- You use PostgreSQL-backed workers, inbox tables, or `FOR UPDATE SKIP LOCKED`.
- You want reliability claims tied to implementation code and integration tests.

## Do not use this when

- You need a drop-in production messaging framework.
- You need end-to-end exactly-once behaviour across HTTP, email, payments, or webhooks.
- You need broker HA, multi-region failover, stream lifecycle management, or throughput-planning guidance.
- You need a Kafka implementation; this repository uses NATS JetStream as one durable transport example.

## Guarantee boundaries & non-goals

The guarantees are deliberately bounded. They are pinned by integration tests against real PostgreSQL and NATS in a **single-node, at-most-two-instance** topology; they are not evidence of production operation at scale.

- **Transactional outbox:** aggregate change and outbox row commit in one transaction.
- **At-least-once publish:** a crash after publish but before marking the row causes a duplicate by design. There is no outbox lease/claim, so multiple publishers may publish the same row.
- **Idempotent inbox ingest:** deduplication assumes a stable `(logical_id, occurred_on_utc)` key and immutable payload for that key.
- **Exactly-once local apply:** only the local database effect and processed mark are covered. HTTP, webhooks, email, payments, and other external side effects are explicitly outside the guarantee.
- **Fault model:** tests inject transaction rollbacks and pre-publish states, plus real NATS redelivery. They do not model process kills, connection loss mid-commit, or broker failure.

Explicit non-goals include outbox poison-message quarantine, global ordering, backpressure and throughput control, JetStream tuning, broker replication and lifecycle management, multi-region failover, independent pub/sub consumer groups, and cross-process trace-context propagation.

## Run and verify

[`DEMO.md`](DEMO.md) is a runnable walkthrough of the durable path, failure-path guarantees, and a two-instance run.

```bash
dotnet build src/ModulithReliabilityKit.sln
dotnet test src/ModulithReliabilityKit.sln

docker compose -f docker-compose.postgres.yml up -d
dotnet run --project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj --urls http://localhost:5099
```

The integration tests use Testcontainers and require Docker. The architecture and reliability-policy unit tests do not.

## Documentation map

- [`docs/README.md`](docs/README.md) — reading order and document ownership.
- [`docs/architecture/`](docs/architecture/) — Mermaid diagrams: a [container view](docs/architecture/container-view.md) of the event flow and a [component / failure-boundary handoff view](docs/architecture/handoff-components.md).
- [`docs/05-events-and-messaging/`](docs/05-events-and-messaging/) — integration events and the reliability matrix.
- [`docs/07-module-architecture/`](docs/07-module-architecture/) — module boundaries and architectural comparison.
- [`docs/09-lessons-learned/`](docs/09-lessons-learned/) — extracted rules and case studies.
- [`docs/10-skeleton/`](docs/10-skeleton/) — what was built and why.
- [`docs-tw/`](docs-tw/) — Traditional Chinese mirror of the notes.

## Scope and provenance

This is a purpose-built reference implementation and written case study, not a copy, fork, or export of a production system. It contains no proprietary source code, data, schemas, credentials, employer or product identifiers, or tenant information.

The architecture is informed by public modular-monolith reference work, especially Kamil Grzybek’s [Modular Monolith with DDD](https://github.com/kgrzybek/modular-monolith-with-ddd). Where this repository diverges, the documentation explains the reason rather than claiming a universal improvement.

The code and documentation were refined with substantial AI assistance. The named claims are deliberately linked to code and focused tests under a documented fault model, so they can be inspected rather than taken on trust.

## Reuse and license

The code namespace remains `ModulithReliabilityKit` for compatibility with the existing solution and commands. Reuse is permitted under the [MIT License](LICENSE), but it is secondary to the repository’s primary purpose: an inspectable, test-backed reliability reference and case study. See [`TEMPLATE.md`](TEMPLATE.md) for rebranding support.
