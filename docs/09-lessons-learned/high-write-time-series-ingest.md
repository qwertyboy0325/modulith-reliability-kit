# Lessons learned: scaling a high-write time-series ingest path

## Purpose

A de-identified design case study from operating a **high-write time-series ingest path** in production
(device/sensor streams landing continuously). It is the piece I spent the most design effort on, so it is
written up here with representative numbers and the trade-offs behind each decision.

This is a **lessons-learned narrative, not a kit feature.** The `ModulithReliabilityKit` skeleton does not
ship a time-series module; every code-grounded claim in these docs stays inside `src/`. This document
captures the *engineering shape* of the problem and the reasoning — the same "reliability under real load"
thesis the kit demonstrates, one layer down at the storage/write path. It carries **no proprietary
identifiers**; scale figures are rounded, representative magnitudes (see the de-identification note in the
[README](../../README.md#scope-provenance-and-ai-assisted-authorship)).

> The figures below are **reconstructed, representative magnitudes** from de-identified production
> experience — not an exported dataset or an independently auditable incident report. They illustrate the
> engineering shape and the reasoning, not a reproducible benchmark.

## Context (the shape of the workload)

- A continuous stream of device telemetry: each device reports position/status every few seconds.
- Two write shapes coexist:
  1. an **append-only history** table (every reading kept for a bounded window), and
  2. a small **"latest state per device"** table (one row per device, constantly overwritten).
- Order of magnitude: **hundreds of millions of writes per day**, and an append-history ingest on the
  order of **~100+ GB/day uncompressed**.

## Problem 1 — write amplification killed the "latest state" table

The latest-state rows were upserted on **every** incoming reading:

```sql
INSERT ... ON CONFLICT (device_key) DO UPDATE SET ...   -- ran on every telemetry packet
```

Under PostgreSQL **MVCC**, an `UPDATE` is not an in-place edit: it marks the old row version dead and
inserts a new one. So an unconditional upsert per packet meant:

- an **~100% update ratio** (essentially every touch was an update, not an insert), and
- **dead tuples climbing past ~80%** of the table, faster than autovacuum could reclaim — degrading
  query latency and bloating the table/indexes.

**Root cause:** it was not just an ops/vacuum problem. It was a **code + architecture** problem: writing
unconditionally, and writing real-time hot state straight to the durable store on the packet path.

## The layered fix (ordered by effort vs. payoff)

| Fix | Idea | Write reduction | Effort |
| --- | ---- | --------------- | ------ |
| **Conditional update** | Only upsert when something *materially* changed — position moved beyond a threshold, speed changed beyond a threshold, or a bounded "heartbeat" interval elapsed | ~60–80% fewer updates | Low (SQL only) |
| **HOT + fillfactor** | Lower `fillfactor` (e.g. 70) and avoid updating indexed columns so PostgreSQL can use Heap-Only-Tuple updates and skip index churn | fewer index writes; complements the above | Low (`ALTER TABLE`) |
| **Write-behind cache** | Keep hot "latest state" in an in-memory store; flush to the durable table on a bounded interval (e.g. every ~30s) instead of per packet | up to ~97% fewer updates | Medium (architecture) |
| **Continuous aggregates** | Let the time-series extension maintain a materialized "latest" view from the history table on a refresh policy, instead of a hand-maintained mutable table | writes become the extension's problem | Medium |

The key idea in the cheap fix is exactly the kit's idempotency instinct applied to writes: **a write that
changes nothing should not happen.** A movement/threshold predicate in the `WHERE` of the `ON CONFLICT DO
UPDATE` turns "write every packet" into "write only real change," which is both correct and dramatically
cheaper.

## Problem 2 — keeping ~100 GB/day of history without going broke

The append-only history is retained for a bounded window but is still huge. Two levers:

- **Columnar compression** (TimescaleDB-style hypertable compression), segmenting by `device_key` and
  ordering by `timestamp DESC`. In practice compression ratios on this shape of data were extreme —
  **~99% and beyond** (multi-GB chunks shrinking to tens/hundreds of KB), because per-device,
  time-ordered numeric/positional data compresses beautifully. Compress on a delay (e.g. after 24–72h) so
  the recent, still-mutating window stays uncompressed and fast.
- **Bounded retention**: drop chunks past the window automatically instead of ever running a giant
  `DELETE`.

### Tiered storage — and the lock that ambushed it

Cost/perf was tuned with a **hot / warm / cold** lifecycle:

| Tier | Media | Age | State |
| ---- | ----- | --- | ----- |
| Hot | SSD | recent days | uncompressed, fast |
| Warm | HDD | weeks–months | compressed (~200:1) |
| Cold | object store (S3) | archive | Parquet (~300:1) |

The subtle bug: the first version moved chunks between tablespaces with `ALTER TABLE ... SET TABLESPACE`,
which takes an **`ACCESS EXCLUSIVE` lock** and blocks production queries for the whole move of a multi-GB
chunk. The redesign made maintenance **lock-aware and non-blocking**:

1. **Phase A** — compress in place (near-non-blocking, on the extension's own schedule).
2. **Phase B** — move only the **already-compressed** chunks (tens of MB, not multi-GB), one at a time,
   with a small delay between chunks, capped to a time budget, run in the off-peak window.

Moving 25 MB instead of 5 GB, spaced out and time-boxed, took the per-chunk stall from "seconds of global
blocking" to sub-second. **Lesson: at scale, the maintenance job's lock profile matters as much as the
feature it maintains.**

## Problem 3 — row width at a billion rows

When rows number in the hundreds of millions to billions, the *width of a row* is a first-class cost:

- **Key type**: `UUID` keys cost 16 bytes each; a `BIGINT` surrogate is 8. Two UUID columns → 32 bytes/row;
  moving to `BIGINT` roughly halves that and shrinks every index that carries the key (order of ~15 GB
  saved per billion rows, plus 30–40% smaller indexes).
- **Field encoding**: a status stored as text (`"10000001"`) that is really a bitmask belongs in a
  `SMALLINT`. Small per-row savings multiply into gigabytes at this row count.

These are "boring" wins, but at a billion rows the row width, not the query, is often the bill.

## How this connects to the kit

- **Idempotent writes** — the conditional-update predicate is the write-path sibling of the kit's
  idempotent inbox (`ON CONFLICT DO NOTHING` ingest): don't apply an effect that has no new information.
- **Bounded, explicit units of work** — flushing hot state on a schedule instead of on the packet path is
  the same instinct as the kit's dedicated background drains with deterministic resource lifetime.
- **Operability is a design input** — just as the kit surfaces retry/dead-letter/reprocess as
  first-class, here the *maintenance* jobs (compression, tiering) are designed for their lock and cost
  profile, not bolted on.

## What to copy

1. **Make writes conditional.** A no-op write is still an MVCC write; guard upserts with a real-change predicate.
2. **Keep hot mutable state off the durable per-event path** when the event rate is high — write-behind on a bounded interval.
3. **Compress + retain by policy**, not by hand-rolled deletes; segment/order compression by your dominant query key.
4. **Design maintenance for its lock profile.** Prefer moving small compressed chunks, time-boxed and spaced, over big blocking operations.
5. **Treat row width as a cost** once you are in the hundreds-of-millions-of-rows regime.

## What not to copy blindly

- The exact thresholds (distance/speed/heartbeat), compression delays, and tier ages are **workload-specific** — measure yours.
- Write-behind caching **trades durability of the hot copy for write volume**; only acceptable because the durable history is the source of truth and the "latest state" is reconstructable.
- Tiered storage adds real operational surface (tablespaces, object-store archival, restore paths). Do not adopt it before the single-tier compressed store is genuinely the bottleneck.

## Next document links

- `08-operational-concerns/observability.md` — metrics/tracing for the reliability path (the numbers you would watch to catch the problems above early).
- `05-events-and-messaging/reliability-matrix.md` — the idempotency/at-least-once reasoning this write-path fix mirrors.
