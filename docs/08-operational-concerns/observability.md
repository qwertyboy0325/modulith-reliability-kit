# Operational concerns: observability

## 1. Purpose

Make the reliability pipeline **observable in production terms**, not just via logs. An operator should be
able to answer, at a glance: are messages flowing, how often do inbox applies fail and retry, is anything
being dead-lettered, and are operators recovering dead-letters? These are exposed as metrics (and spans)
straight from the code paths that already carry the reliability guarantees.

## 2. Files / patterns inspected

- `BuildingBlocks.Infrastructure/Diagnostics/ReliabilityMetrics.cs` — the domain-specific instruments.
- `BuildingBlocks.Infrastructure/Diagnostics/ReliabilityInstrumentation.cs` — well-known meter / activity-source names.
- `BuildingBlocks.Infrastructure/Processing/OutboxProcessorBase.cs` — outbox counters + span.
- `Modules/Notifications/…/Processing/NotificationsInboxProcessor.cs` — inbox counters + span.
- `Modules/Notifications/…/Inbox/InboxDeadLetterReprocessor.cs` — operator-recovery counter.
- `BuildingBlocks.Infrastructure/Events/NatsEventBus.cs` + `NatsSubscriptionBackgroundService.cs` — transport counters + spans.
- `Api/ModulithReliabilityKit.Api/Program.cs` — OpenTelemetry wiring + `/metrics`.

## 3. Actual implementation summary

Instrumentation uses the BCL `System.Diagnostics.Metrics.Meter` / `ActivitySource` (no library lock-in in
the processors). A single `ReliabilityMetrics` singleton is shared by every processor. The metrics are:

| Metric | Type | Meaning |
| ------ | ---- | ------- |
| `messaging.outbox.published` | counter | outbox rows published to the bus (tag: `module`) |
| `messaging.outbox.publish_failures` | counter | outbox publish attempts that threw |
| `messaging.outbox.process.duration` | histogram (ms) | time to publish one outbox message |
| `messaging.inbox.processed` | counter | inbox messages applied exactly once |
| `messaging.inbox.retried` | counter | inbox messages scheduled for retry after a failure |
| `messaging.inbox.dead_lettered` | counter | inbox messages moved to the dead-letter table |
| `messaging.inbox.process.duration` | histogram (ms) | time to apply one inbox message |
| `messaging.inbox.dead_letter.reprocessed` | counter | dead-letters requeued by an operator |
| `messaging.transport.published` | counter | events published on the durable (NATS) transport |
| `messaging.transport.redelivered` | counter | transport deliveries nacked for redelivery |

Spans (`ActivitySource` = `ModulithReliabilityKit.Reliability`): `outbox.publish`, `inbox.process`,
`nats.publish`, `nats.consume`. Failures set the span status to error.

The **API host** wires OpenTelemetry: metrics (the custom meter + ASP.NET Core + runtime) are exported on
a Prometheus scraping endpoint at `GET /metrics`; traces are exported via OTLP **only when**
`Observability:OtlpEndpoint` is configured (otherwise spans are recorded but not shipped, so no collector
is required for the default run).

## 4. Flow

```text
outbox drain ──▶ ReliabilityMetrics.OutboxPublished / duration ─┐
inbox drain  ──▶ processed | retried | dead_lettered / duration ├─▶ Meter "…Reliability" ─▶ OTel ─▶ /metrics (Prometheus)
reprocess    ──▶ dead_letter.reprocessed                        │                                └─▶ OTLP traces (opt-in)
NATS bus     ──▶ transport.published / redelivered ─────────────┘
```

## 5. What to copy

- **Instrument the reliability outcomes, not just request latency.** Retry rate and dead-letter count are
  the numbers that predict incidents in an async system.
- **Keep instrumentation in Infrastructure using BCL types.** The processors depend on `Meter`/`ActivitySource`
  only; the exporter choice lives in the host, so swapping Prometheus/OTLP never touches domain code.
- **Record from the same code path the guarantee lives in**, so a metric can never drift from behavior.

## 6. What not to copy blindly

- The **Prometheus AspNetCore exporter is a prerelease** package (`1.16.0-beta.1`); the OpenTelemetry
  metrics API it builds on is stable. If you need only stable packages, export via OTLP to a collector
  instead and drop the `/metrics` endpoint.
- Tags are intentionally **low-cardinality** (`module`, `transport`). Do not add per-message IDs as tags.
- These are **counts and durations**, not gauges of queue depth. A pending-depth gauge (observable
  instrument over the inbox/outbox tables) is a reasonable next addition.

## 7. Open questions / next

- Pending-depth observable gauges (outbox unprocessed, inbox pending, open dead-letters).
- Distributed trace-context propagation **through** NATS headers (currently spans are per-process).
- A ready-made Grafana dashboard / alert rules for retry-rate and dead-letter thresholds.

## 8. Next document links

- `05-events-and-messaging/reliability-matrix.md` — the guarantees these metrics observe.
- `01-foundation/building-blocks.md` — where the processors and bus live.
