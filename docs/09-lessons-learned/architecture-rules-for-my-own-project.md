# Architecture Rules For My Own Project

## Purpose

A concrete, opinionated rule set for building a modular-monolith / DDD backend, derived from what works and
what commonly goes wrong. These are decisions to make *before* writing code, with typical failure modes as
evidence. The rules are anchored on this kit's own design (Catalog producer, Notifications consumer).

Direct and critical by design.

## Source basis

All rules trace to findings in:
- `01-foundation/building-blocks.md`
- `01-foundation/dependency-injection.md`
- `02-application-pipeline/unit-of-work.md`
- `05-events-and-messaging/integration-events.md`
- `05-events-and-messaging/reliability-matrix.md`

---

## 1. Patterns worth copying (as-is or nearly)

1. **Layout: `BuildingBlocks` + `Modules/<context>/{Domain,Application,Infrastructure,IntegrationEvents}` + thin API host.**
   Clean, proven, scales without rot.
2. **Separate `IntegrationEvents` project per producer module as the only cross-module contract.** Other
   modules may reference *only* this project. Enforced by architecture tests.
3. **Domain primitives in BuildingBlocks:** `Entity` (with domain-event list + `CheckRule`), `IAggregateRoot`,
   `IBusinessRule` + business-rule exception, domain-event base.
4. **Three-level event model:** domain event → domain-event *notification* → integration event. It cleanly
   separates "what happened in the domain" from "what we tell other modules".
5. **Transactional outbox in one transaction:** aggregate changes + outbox rows committed atomically.
6. **Outbox processor with `FOR UPDATE SKIP LOCKED`, batched, idempotent marking.** Concurrency-safe.
7. **Inbox with idempotent ingest (`ON CONFLICT DO NOTHING`) + retry + dead-letter.** The mature core of
   reliable consumption. Copy it wholesale.
8. **Command pipeline as pipeline behaviors** (validation → unit-of-work → logging) so handlers stay pure.
9. **Handlers never call `SaveChanges`** — the unit-of-work behavior owns commit.
10. **A first-class execution/correlation context accessor.**

## 2. Patterns to copy only with changes

1. **Unit of Work — make the transaction explicit.** Relying on EF's implicit single-`SaveChanges`
   transaction breaks silently the moment a command writes via both EF and a second path. → Open an explicit
   transaction (this kit does), and enrol every write in it, or forbid mixing within one command.
2. **Domain-event dispatch timing.** If in-process domain-event handlers have external side effects, either
   dispatch after a successful save, or guarantee handlers only mutate the same context.
3. **Value objects.** Reflection-based value objects are slow. → Prefer C# `record` value objects; keep a
   reflection base only where genuinely needed.
4. **Strongly-typed IDs.** Pick **one** style from day one. Two coexisting styles (a legacy base + records)
   is pure debt.
5. **Logging.** Use **one** mechanism (a pipeline behavior *or* a decorator, not both) and apply it to every module.
6. **No-op interface methods** (e.g. an outbox `Save()` that does nothing) → drop them or make them meaningful.
7. **Internal-commands queue.** Useful, but only adopt it when a real delayed/retryable operation exists, and
   give its processor the same rigor as the outbox (`SKIP LOCKED`, real failure handling).

## 3. Patterns NOT mature enough to copy directly

1. **An in-memory `IEventsBus` as the reliable cross-module transport.** A process-wide dictionary has no
   subscriber-less delivery and no cross-process durability. Durability comes only from outbox/inbox, not the
   bus. → Do not treat `IEventsBus.Publish` as reliable delivery; put a broker behind it if multi-process is plausible.
2. **Direct `IEventsBus.Publish`, especially after `commit`.** This produces "DB changed, nobody notified, no
   record" failure modes. Acceptable *only* for explicitly droppable events.
3. **Inconsistent consumer model** (some events via a durable inbox, others direct-to-MediatR with swallowed
   errors). → Decide one model and enforce it.
4. **Static-mutable composition-root holders** (a per-module `static IContainer`). Global singletons that break
   test isolation and hide the graph; they also breed leaked lifetime scopes.
5. **The per-module container model** (independent module containers sharing only instances). Heavy and
   leak-prone. → Default to a single container with module-scoped registrations + arch tests; adopt per-module
   containers only with a hard runtime-isolation requirement you can name.
6. **An untested/broken module generator** (e.g. one that references a template directory that doesn't exist).
   Don't copy a generator without its template, and test it in CI.
7. **Empty/scaffolded `IntegrationEvents` projects** — dead projects; ship contracts only where they exist.

## 4. Rules I will enforce from day one

1. **All state-changing use cases go through command handlers.** No business writes from controllers or services.
2. **Command handlers must not call `SaveChanges`/commit directly.** The unit-of-work behavior commits.
3. **Unit of Work defines the consistency boundary, and the transaction is explicit.** One command = one
   transaction = aggregate + outbox. No mixing EF with a second store inside a command unless both enrol in the same transaction.
4. **Durable cross-module events use the Outbox.** The outbox path is the default; direct publish is the exception.
5. **Best-effort/direct publish is allowed only if the event carries an explicit `Reliability: BestEffort` classification and is documented.** Default is durable.
6. **Every integration event has a reliability-matrix row before merge** (publisher, path, consumers, durable?, can-drop?, retry?, inbox?). No event ships "Unknown".
7. **Consumers of durable events must use the inbox and be idempotent.** No direct-to-MediatR-with-swallowed-errors for events that matter.
8. **Inbox handlers must be idempotent** (dedup on `(logical_id, occurred_on)`), with retry + dead-letter.
9. **No direct calls between modules.** Cross-module communication is only via integration-event contracts in
   the `IntegrationEvents` project. Foreign references allowed *only* from the Application layer, never Infrastructure/Domain.
10. **Application never references BuildingBlocks.Infrastructure.** Layer direction is law.
11. **New modules follow a fixed, *working* template**, and any generator is tested in CI.
12. **One strongly-typed-ID style, one logging mechanism, one validation mechanism** — repo-wide, enforced by arch tests.
13. **Do not swallow exceptions in the bus or consumers silently.** Failures must surface (dead-letter, metric, alert).
14. **Arch tests gate boundaries** (module isolation, layer direction, "handlers don't call SaveChanges", "no `IEventsBus.Publish` outside approved sites").

## 5. Questions to answer before implementation

1. **Single process forever, or eventual multi-process?** If multi-process is even plausible, design `IEventsBus`
   behind a broker abstraction *now*; an in-memory-only choice is a wall.
2. **One container or per-module containers?** Default single + arch tests. Only go per-module with a named reason.
3. **EF-only, or EF + another store?** If a second store is allowed for writes, define the transaction story up front (rule 3).
4. **Which events are correctness-critical vs. best-effort?** This determines outbox-vs-direct per event.
5. **Background processing cadence and idempotency?** Ensure jobs are single-flighted and idempotent.
6. **How are dead-letters triaged?** A dead-letter table needs an owner who watches it.

## 6. Minimal architecture skeleton to build first (bottom-up)

Build in this order; do not start business features until step 6.

1. **BuildingBlocks.Domain:** `Entity` (+ domain events + `CheckRule`), `IAggregateRoot`, domain-event base,
   `IBusinessRule` + exception, one strongly-typed-ID style.
2. **BuildingBlocks.Application:** `IEventsBus`, `IntegrationEvent`, domain-event notifications,
   `IOutbox`/`OutboxMessage`, inbox contracts + retry policy, execution context, standard exceptions, paging.
   *(Single namespace — do not ship duplicate contract definitions.)*
3. **BuildingBlocks.Infrastructure:** unit-of-work **with explicit transaction**, domain-events dispatcher +
   accessor + explicit notification mapper, inbox processing + dead-letter, one logging mechanism, typed-ID EF converters.
4. **Composition root:** one container; per-command lifetime scope; pipeline behaviors
   (Validation → UnitOfWork → Logging); MediatR handler scan. Arch tests for boundaries.
5. **Messaging runtime:** transactional outbox processor (`SKIP LOCKED`, batched) + inbox processor
   (idempotent, retry, dead-letter) + a broker-ready `IEventsBus` abstraction (in-memory impl for dev is fine,
   behind the interface). Reliability-matrix template in `docs/`.
6. **One reference producer + one consumer** built end-to-end following a *working* template: a Domain
   aggregate, command + handler + validator, repository (interface in Domain, EF impl in Infrastructure),
   `DbContext` with its own schema, one durable integration event (outbox publish → inbox consume), arch tests.
   In this kit that is Catalog (`Product` → `ProductCreatedIntegrationEvent`) → Notifications.

### Skeleton diagram

```text
BuildingBlocks
  Domain ──────────────► (entities, events, rules, typed-ids)
  Application ─────────► (IEventsBus, IntegrationEvent, IOutbox, inbox retry policy, execution context, UoW contract)
  Infrastructure ──────► (UnitOfWork[explicit tx], dispatcher, inbox+deadletter, logging, outbox/inbox processors)
        ▲
        │ depends on
Modules (Catalog producer, Notifications consumer)
  Domain → Application → Infrastructure ; IntegrationEvents (public contracts on the producer)
        ▲
        │ composed by
API host (single container, pipeline behaviors, arch tests)
```

## What to copy / not copy (one-line recap)

- **Copy:** layout, contract-project boundary, three-level event model, transactional outbox, inbox+dead-letter,
  pipeline behaviors, handlers-don't-save.
- **Copy with changes:** explicit UoW transaction, record value-objects/IDs, single logging/validation mechanism.
- **Do not copy:** in-memory bus as reliable transport, direct publish for critical events, swallowed errors,
  static composition-root holders, per-module container model by default, broken module generator, dead scaffolding.

## Open questions

- Is per-module physical isolation a real requirement for my project, or am I cargo-culting it?
- How much operational machinery (metrics, dead-letter triage) do I actually need on day one?

## Next (future passes)

- `03-domain-model/` (entities, value objects, domain-event dispatching depth)
- `04-persistence/` (per-module DbContext, migrations, Dapper-vs-EF boundary)
- `06-background-processing/` (outbox/inbox scheduling, internal commands)
- `07-module-architecture/` (boundaries, startup, a *fixed* module template)
- `08-operational-concerns/` (logging, retry/dead-letter ops, observability, worker status)
