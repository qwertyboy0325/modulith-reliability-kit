# Architecture Notes: A Modular Monolith Reliability Kit

This documentation set records the architecture and reliability patterns behind the
`ModulithReliabilityKit` skeleton in this repository. It distils reusable modular-monolith / DDD
lessons into a **build guide** for a new modular-monolith backend, anchored on this repo's own
neutral sample domain (a `Catalog` producer module and a `Notifications` consumer module).

It is written from the bottom up: foundations first, business features last (and mostly out of scope).

Where useful, it draws on publicly available prior art — notably
**Kamil Grzybek's "Modular Monolith with DDD" (public reference,
[github.com/kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd))** —
which is attributed inline. All concrete code claims are tied to this repo's `src/` skeleton.

---

## 1. Purpose

- Capture **reusable, code-grounded** architecture patterns for a modular monolith.
- Separate **mature/copyable** patterns from **risky/redesign-before-copy** ones.
- Produce a concrete **architecture rule set** for a new project.

This is not a feature tour and not marketing. Every concrete claim is tied to a file under
this repo's `src/` skeleton; public prior art is attributed as such.

## 2. How to read these documents

Each document follows a fixed shape:

1. **Purpose**
2. **Files / patterns inspected**
3. **Actual implementation summary** (what the code does, not what it intends)
4. **Flow diagram** (text) where useful
5. **What to copy**
6. **What not to copy blindly**
7. **Open questions**
8. **Next document links**

When behavior is inconsistent, the inconsistency is documented explicitly rather than smoothed over.
Three labels are used throughout:

- **ACTUAL** — what the code does today.
- **INTENT** — what the structure appears designed to do.
- **VERDICT** — copy / copy-with-changes / do-not-copy, with reasoning.

## 3. Recommended reading order

1. `00-orientation/project-map.md`
2. `00-orientation/source-code-reading-order.md`
3. `01-foundation/building-blocks.md`
4. `01-foundation/dependency-injection.md`
5. `02-application-pipeline/unit-of-work.md`
6. `05-events-and-messaging/integration-events.md`
7. `05-events-and-messaging/reliability-matrix.md`
8. `07-module-architecture/catalog-module-best-practices.md`
9. `07-module-architecture/modular-monolith-ddd-comparison.md`
10. `08-operational-concerns/observability.md`
11. `09-lessons-learned/architecture-rules-for-my-own-project.md`
12. `09-lessons-learned/high-write-time-series-ingest.md`
13. `09-lessons-learned/inbox-stale-failure-write-race.md`

## 4. What this documentation is NOT

- **Not** a business-feature catalog (domain feature logic is out of scope for this pass).
- **Not** an endorsement. Real-world modular monoliths contain inconsistencies and reliability gaps; they are flagged as lessons.
- **Not** complete. This is the **first bottom-up pass**. Later passes are listed in each document's "Next" section and in the final summary.
- **Not** a copy-paste source. Some patterns are explicitly marked "do not copy directly".

## 5. Folder taxonomy (and why)

The proposed taxonomy was kept, with minor adjustments justified below.

```text
docs/
  README.md
  00-orientation/
    project-map.md
    source-code-reading-order.md
  01-foundation/
    building-blocks.md
    dependency-injection.md
  02-application-pipeline/
    unit-of-work.md
  05-events-and-messaging/
    integration-events.md
    reliability-matrix.md
  07-module-architecture/
    catalog-module-best-practices.md
    modular-monolith-ddd-comparison.md
  08-operational-concerns/
    observability.md
  09-lessons-learned/
    architecture-rules-for-my-own-project.md
    high-write-time-series-ingest.md
    inbox-stale-failure-write-race.md
  10-skeleton/
    implementation-decisions.md
    build-log.md
    applied-improvements-and-postgres-demo.md
```

Adjustments vs. the originally proposed structure:

- **Only the documents grounded in code were created.** Empty stub files for
  `03-domain-model/`, `04-persistence/`, `06-background-processing/`
  were intentionally **not** created to avoid documenting things not yet grounded in code. They are
  listed as the next pass. `08-operational-concerns/observability.md` now exists (metrics + tracing).
- The numbering gaps (no `03`, `04`, `06` yet) are deliberate and match the proposed taxonomy,
  so later passes slot in without renumbering.
- `solution-structure.md` / `composition-root.md` content was **folded into**
  `01-foundation/dependency-injection.md` because the composition root and the DI strategy are best
  read together.

## 6. Documentation status

| Area                     | Status | Notes                                                                                       |
| ------------------------ | ------ | ------------------------------------------------------------------------------------------- |
| Project map              | Draft  | Source-folder ownership mapped from `src/` layout                                            |
| Reading order            | Draft  | Bottom-up path defined for a new engineer                                                    |
| Foundation (BuildingBlocks) | Draft | Grounded in `src/BuildingBlocks`; event-contract and domain-coupling trade-offs flagged |
| Dependency Injection     | Draft  | Single MS.DI container with typed per-module persistence documented                          |
| Unit of Work             | Draft  | Explicit-transaction UoW; dispatch-events-then-SaveChanges documented                        |
| Integration events       | Draft  | Outbox publish vs direct publish paths; in-memory bus only                                   |
| Reliability matrix       | Draft  | Per-event classification template; best-effort vs durable decision recorded; dead-letter recovery + multi-instance (`SKIP LOCKED`) findings added |
| Lessons learned / rules  | Draft  | Personal rule set extracted; critical. De-identified case study added: high-write time-series ingest (write amplification, tiered storage, row width). Inbox stale-failure write race fixed test-first (concurrency bug found by claim audit; reproducible red preserved at its commit, pinned by a regression test) |
| Skeleton implementation  | Draft | `src/ModulithReliabilityKit` skeleton + Catalog/Notifications modules; typed module persistence + PostgreSQL demo/migration runbook added |
| Domain model             | Not started | Next pass (entities, VOs, domain-event dispatching depth)                              |
| Persistence (EF/Dapper)  | Not started | Next pass (per-module DbContext, migrations, Dapper-vs-EF split)                       |
| Background processing    | Not started | Next pass (outbox/inbox scheduling, internal commands depth)                          |
| Module architecture      | Draft | Catalog sample module plus public modular-monolith comparison; facade + typed persistence priorities implemented |
| Operational concerns     | Draft | Retry/dead-letter, dead-letter recovery, and multi-instance safety in `05-events-and-messaging`; observability (reliability metrics via Prometheus `/metrics` + opt-in OTLP traces) documented in `08-operational-concerns/observability.md` |

---

## Source of truth

All concrete code claims in these documents are relative to the repository root and point into the
`src/` skeleton. Public prior art is attributed as such. If a claim cannot be traced to a file, it is
marked **Unknown**.
