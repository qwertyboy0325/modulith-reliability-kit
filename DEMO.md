# Demo walkthrough

A ~5-minute, runnable tour of the reliability story. Part 1 shows the durable path **live**
(HTTP + SQL). Part 2 proves the guarantees you cannot fake with a happy-path click-through —
crash recovery, dead-letter, recovery, and multi-instance safety — by running the tests that pin
them. Every step maps to a row in the README
[verification map](README.md#verify-the-claims-guarantee--code--test).

> Recording tip: keep two panes open — a terminal running the API, and a second terminal for
> `curl` + `psql`. Narrate each step with the guarantee it demonstrates (called out below).

## Prerequisites

- .NET 8 SDK
- Docker (for PostgreSQL and the integration tests)

## Part 1 — The durable path, live (~2 min)

**Talking point:** *durable publish ≠ durable delivery.* We watch a write travel Catalog → outbox →
in-memory bus → Notifications inbox → applied effect, each hop with its own guarantee.

### 1. Start PostgreSQL and the API

```bash
docker compose -f docker-compose.postgres.yml up -d
dotnet run --project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj --urls http://localhost:5099
```

The API applies both module migrations on boot. Background drains run on a timer:
**outbox every 10s, inbox every 5s** (so effects are *eventually* consistent, not instant).

### 2. Create a product (Catalog)

```bash
curl -s -X POST "http://localhost:5099/catalog/products/" \
  -H "Content-Type: application/json" \
  -d '{"name":"Demo Product","price":12.50,"currency":"usd"}'
# → {"id":"<PRODUCT_ID>"}
```

**Guarantee (atomic):** the aggregate row and the outbox row commit in **one** transaction.
Immediately after the POST, before the drain tick, the outbox row exists and is *unprocessed*:

```bash
docker exec -it modulith_reliability_kit-postgres \
  psql -U modulith_reliability_kit -d modulith_reliability_kit \
  -c "SELECT * FROM catalog.outbox_messages ORDER BY id;"
# processed_on_utc IS NULL  ← published intent is durable, delivery hasn't happened yet
```

### 3. Watch the message become durable on the consumer side

Wait ~10–15s for the outbox + inbox drains, then look at the inbox:

```bash
docker exec -it modulith_reliability_kit-postgres \
  psql -U modulith_reliability_kit -d modulith_reliability_kit \
  -c "SELECT id, type, status, retry_count, processed_on_utc FROM notifications.inbox_messages ORDER BY id;"
# status = 'processed', retry_count = 0
```

**Guarantee (idempotent ingest + exactly-once apply):** the inbox row is unique per
`(logical_id, occurred_on_utc)`, and its business effect + `processed` mark committed together.

### 4. Read the applied effect through the API

```bash
curl -s "http://localhost:5099/notifications/product-announcements"
# → exactly one announcement for the product you created
```

**Talking point:** the announcement is owned by the **Notifications** module and only ever reached it
via the public `IntegrationEvents` contract — never a direct cross-module call.

### 5. The dead-letter admin surface (recovery loop)

```bash
curl -s "http://localhost:5099/notifications/inbox/dead-letters?includeResolved=true"
# → []  (nothing poisoned on the happy path)
```

The recovery endpoint `POST /notifications/inbox/dead-letters/{id}/reprocess` requeues a poisoned
message once its downstream cause is fixed. Triggering a *real* dead-letter live needs an induced
failure, so its full lifecycle is demonstrated by the test in Part 2 (§ dead-letter + recovery).

## Part 2 — The hard guarantees, proven by tests (~2–3 min)

**Talking point:** the interesting reliability properties are failure paths. They are pinned against a
**real PostgreSQL** (Testcontainers), so they are checkable, not asserted in prose.

### Fast checks (no Docker): boundaries + retry policy

```bash
dotnet test src/Tests/ModulithReliabilityKit.ArchitectureTests/ModulithReliabilityKit.ArchitectureTests.csproj
dotnet test src/Tests/ModulithReliabilityKit.ReliabilityTests/ModulithReliabilityKit.ReliabilityTests.csproj
```

- **Architecture tests** — module isolation + layer direction are enforced, not just documented.
- **Retry-policy unit tests** — back-off then dead-letter after N attempts.

### Reliability integration tests (Docker): the failure paths

```bash
dotnet test src/Tests/ModulithReliabilityKit.IntegrationTests/ModulithReliabilityKit.IntegrationTests.csproj
```

Narrate what the key tests prove (all against real PostgreSQL):

| Test | Guarantee demonstrated |
| ---- | ---------------------- |
| `CatalogOutboxReliabilityTests.Unpublished_Outbox_Row_Is_Republished_Exactly_Once_After_Restart` | Outbox survives a crash and re-publishes exactly once |
| `NotificationsInboxReliabilityTests.Duplicate_Delivery_Produces_Exactly_One_Inbox_Row_And_One_Effect` | At-least-once delivery is absorbed idempotently |
| `NotificationsInboxReliabilityTests.Crash_After_Staging_Effect_Rolls_Back_And_Recovers_Exactly_Once` | Crash mid-apply rolls back, then recovers exactly once |
| `NotificationsInboxReliabilityTests.Repeated_Failures_Dead_Letter_The_Message_After_Max_Attempts` | A poison message is dead-lettered, not silently lost |
| `InboxDeadLetterReprocessTests` | Dead-letter **recovery**: requeue → apply exactly once → resolve atomically; re-runs are no-ops |
| `InboxConcurrencyReliabilityTests` | **Multi-instance safe**: `FOR UPDATE SKIP LOCKED` claim ⇒ a concurrent drainer skips a claimed row (no double effect, no spurious failure) |
| `CrossModuleReliabilityE2ETests` | Create ⇒ durable consume ⇒ exactly one announcement, across modules |
| `CatalogToNotificationsHttpE2ETests` | The same story through the real HTTP host |

To spotlight one during recording, filter to it, e.g. the multi-instance safety test:

```bash
dotnet test src/Tests/ModulithReliabilityKit.IntegrationTests/ModulithReliabilityKit.IntegrationTests.csproj \
  --filter "FullyQualifiedName~InboxConcurrencyReliabilityTests"
```

## Part 3 — Optional: the durable, cross-process transport (NATS JetStream)

**Talking point:** the default bus is in-memory (single process). The kit also ships an **opt-in**
JetStream-backed transport so the same outbox/inbox guarantees hold **across processes** — a publish is
persisted against a `PubAck` before it is considered delivered, and delivery is at-least-once via a
durable consumer.

```bash
# NATS (JetStream) is included in the compose file; bring everything up:
docker compose -f docker-compose.postgres.yml up -d

# Run the API on the NATS transport instead of in-memory:
Messaging__Transport=Nats \
  dotnet run --project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj --urls http://localhost:5099
```

The transport guarantees are pinned against a real NATS server by
`NatsCrossProcessReliabilityTests` (a message published while no subscriber is running is still
delivered once one starts; a failed handler is redelivered). Implementation:
`BuildingBlocks.Infrastructure/Events/NatsEventBus.cs` + `NatsSubscriptionBackgroundService.cs`.

### Two instances → exactly-once (SKIP LOCKED + JetStream, the capstone)

**Talking point:** this is the whole thesis in one shot. Run **two** API instances against the **same**
NATS and the **same** PostgreSQL. Every product still yields **exactly one** announcement — even though
both instances drain the shared outbox (at-least-once *publish*, possibly duplicated), JetStream is
at-least-once *delivery*, and both instances drain the shared inbox concurrently. Two independent
duplication sources, collapsed to one effect by the **idempotent inbox** + **`FOR UPDATE SKIP LOCKED`**.

```bash
# 0. Shared infra (Postgres + NATS/JetStream) up.
docker compose -f docker-compose.postgres.yml up -d

# 1. Instance A. Wait until it logs "Applying ... migrations" and GET /health returns Healthy
#    BEFORE starting B (startup migration is not concurrency-guarded; let A migrate first).
Messaging__Transport=Nats \
  dotnet run --project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj --urls http://localhost:5099

# 2. Instance B (second terminal). B's migration is a no-op since A already applied it.
Messaging__Transport=Nats \
  dotnet run --project src/Api/ModulithReliabilityKit.Api/ModulithReliabilityKit.Api.csproj --urls http://localhost:5098
```

Both instances log a bound durable consumer (same durable name ⇒ JetStream delivers each message to
**one** of them, queue-group style):

```
NATS durable consumer modulith-kit-ProductCreatedIntegrationEvent bound to subject integration-events.ProductCreatedIntegrationEvent
```

Create a batch of products (mix the target ports to prove it does not matter which instance receives the write):

```bash
for i in $(seq 1 10); do
  port=$([ $((i % 2)) -eq 0 ] && echo 5099 || echo 5098)
  curl -s -X POST "http://localhost:$port/catalog/products/" \
    -H "Content-Type: application/json" \
    -d "{\"name\":\"MP-$i\",\"price\":$i,\"currency\":\"usd\"}" > /dev/null
done
```

Wait ~15s for the outbox (10s) + inbox (5s) drains, then check the **exactly-once** invariant:

```bash
docker exec -it modulith_reliability_kit-postgres \
  psql -U modulith_reliability_kit -d modulith_reliability_kit -c "
SELECT
  (SELECT count(*) FROM catalog.products)                                   AS products_created,
  (SELECT count(*) FROM notifications.inbox_messages)                       AS inbox_rows,
  (SELECT count(*) FROM notifications.inbox_messages WHERE status<>'processed') AS not_processed,
  (SELECT count(*) FROM notifications.product_announcements)                AS announcements,
  (SELECT count(DISTINCT product_id) FROM notifications.product_announcements) AS distinct_products;"
```

**Expected (on a clean DB, 10 products):** all four counts are **equal** and `not_processed = 0` —
`products_created = inbox_rows = announcements = distinct_products` (= 10 here). One inbox row and one
announcement per product, no duplicates, despite duplicate publishes and two concurrent drainers. **That
equality is the proof** (it holds regardless of the starting count).

Optional — see how the two instances split the apply work via the metrics counter:

```bash
curl -s http://localhost:5099/metrics | grep '^messaging_inbox_processed_total'
curl -s http://localhost:5098/metrics | grep '^messaging_inbox_processed_total'
# The two counters sum to the number of applied messages. The split is timing-dependent: with a small
# batch one instance often claims all pending rows in a single tick (so the other shows 0). SKIP LOCKED
# guarantees no row is applied twice — not an even split. Under sustained load both instances share.
```

Each half of this is pinned by a test: JetStream durable/at-least-once delivery by
`NatsCrossProcessReliabilityTests`, and concurrent inbox drain (`FOR UPDATE SKIP LOCKED`, exactly-once
apply, no drops) by `InboxConcurrencyReliabilityTests`. This part runs the two together, live.

## Part 4 — Observe the reliability metrics (~30s)

**Talking point:** the interesting numbers in an async system are the failure paths — retry rate and
dead-letter count — not just request latency. They are emitted straight from the drain code and scrapeable.

```bash
# After creating a product and letting the drains run (Part 1):
curl -s http://localhost:5099/metrics | grep '^messaging_'
# e.g. messaging_outbox_published_total{module="catalog"} 1
#      messaging_inbox_processed_total{module="notifications"} 1
#      messaging_inbox_process_duration_milliseconds_* (histogram)
```

Counters cover publish / processed / **retried** / **dead_lettered** / dead-letter **reprocessed** /
transport **redelivered**. Traces (spans `outbox.publish`, `inbox.process`, `nats.*`) export via OTLP when
`Observability:OtlpEndpoint` is set. Details:
[`docs/08-operational-concerns/observability.md`](docs/08-operational-concerns/observability.md).

## Suggested 3-minute recording script

1. **(20s) Framing.** "Modular monolith, DDD. The thesis: durable publish is not durable delivery.
   Every hop has a different guarantee, and each one is pinned by a test." Show the README diagram.
2. **(70s) Live path.** POST a product → show the unprocessed outbox row → wait for the drain →
   show the inbox row `processed` → `GET /product-announcements` returns exactly one. Call out
   *atomic write*, *at-least-once publish*, *idempotent + exactly-once apply*.
3. **(70s) Failure paths.** Run the integration tests; walk the table above, pausing on
   crash-recovery, dead-letter → recovery, and multi-instance `SKIP LOCKED`.
4. **(20s) Close.** "Every claim links to code and a test — check it, don't trust it." Point at the
   README verification map.

## Further reading — design at scale (optional talking point)

The same "reliability under real load" thesis, one layer down at the storage/write path, is written up
as a de-identified case study:
[`docs/09-lessons-learned/high-write-time-series-ingest.md`](docs/09-lessons-learned/high-write-time-series-ingest.md).
Worth a 20–30s mention on camera — it shows the reasoning behind a high-write time-series ingest path
(MVCC write amplification, conditional/idempotent writes, write-behind, columnar compression, lock-aware
tiering, row-width economics), which is the part that took the most design effort.

## Teardown

```bash
docker compose -f docker-compose.postgres.yml down       # add -v to also drop the data volume
```
