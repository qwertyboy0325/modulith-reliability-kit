# Inbox Stale-Failure Write Race (found by claim audit)

## Status

**Fixed**, and pinned by a regression test. `RecordFailureAsync` now re-claims the row under `FOR UPDATE`
and no-ops if it was already processed by a concurrent drainer. The failing state is **preserved for
reference**: it was committed red first (commit `6bd3018`) and fixed in the commit that follows, so the bug
is reproducible by checking out the red commit — see
[Reproduction](#reproduction-test-first-deterministic).

## Fast takeaway

`FOR UPDATE SKIP LOCKED` can serialize the worker that applies an inbox effect.

It does not make later state writes safe by itself.

Any later retry, dead-letter, or bookkeeping transaction must re-establish the row invariant before it writes.

## Evidence timeline

1. A deterministic regression was committed red: `6bd3018`.
2. The fix re-claimed the row under a blocking `FOR UPDATE`: `7d37540`.
3. The same test is green on `main`.

## Purpose

Record a concrete concurrency bug in the inbox *failure-recording* path: what interleaving triggers it, why
the business effect stays correct anyway, what it *does* corrupt, and the minimal fix that closes it.

## Where

- `Notifications.Infrastructure/Processing/NotificationsInboxProcessor.cs` — `ProcessOneCoreAsync` (claim +
  apply) and `RecordFailureAsync` (the failure bookkeeping that runs after a rollback).

## The mechanism

The happy path claims a row under a Postgres row lock and commits the effect + `processed` mark in one
transaction:

```text
SELECT ... WHERE id = ? AND processed_on_utc IS NULL FOR UPDATE SKIP LOCKED
  → dispatch → set processed_on_utc + status = 'processed' → COMMIT
```

On failure the transaction rolls back (releasing the lock) and, in a **separate** transaction,
`RecordFailureAsync` reads the row **by id only** — no `FOR UPDATE`, no `processed_on_utc` check — and
overwrites its status to `retrying` or `dead_letter`.

Interleaving with two drainers A and B (a legitimate multi-instance deployment):

1. A claims the row (`FOR UPDATE SKIP LOCKED`), dispatch throws, A rolls back → lock released.
2. B claims the same row (still `processed_on_utc IS NULL`), dispatch succeeds, B commits `processed`.
3. A now runs `RecordFailureAsync`, reads the row **without a lock or a processed check**, and writes
   `retrying`/`dead_letter` + bumps `retry_count` on top of B's success.

The window is small (A's rollback → A's next transaction → A's read) but real under concurrency, and fully
reachable deterministically with a test gate.

## What is NOT corrupted (why it is bounded)

The business effect stays exactly-once. The claim query filters `AND processed_on_utc IS NULL`; B already set
`processed_on_utc`, and A's failure write never clears it. So the next drain's claim returns no row and the
effect is never re-applied. **No double effect** — this is a state/observability bug, not a double-execution
bug.

## What IS corrupted

- **Row status lies.** The row ends `status = 'retrying'` (or `dead_letter`) while `processed_on_utc IS NOT
  NULL` — a contradictory state that never returns to `processed`. A `retrying` zombie is re-selected on every
  drain, claims nothing, and spins as a no-op forever.
- **Metrics lie.** The inbox *processed* counter was incremented by B while the *retried* / *dead-lettered*
  counter is incremented by A for the **same** message.
- **Spurious dead-letter.** If A's retry budget was already exhausted, A inserts a dead-letter record for a
  message that actually succeeded. `inbox_dead_letters` has no uniqueness on `(logical_id, occurred_on_utc)`,
  so an operator can see — and reprocess — a "poison" message that was fine.

## Claim impact (before the fix)

Before the fix, the interleaving contradicted two written claims:

- README verification map: "…applied exactly once (no double effect, **no spurious failure**)".
- `05-events-and-messaging/integration-events.md`: the note that `FOR UPDATE SKIP LOCKED` prevents
  "record a spurious failure".

`SKIP LOCKED` prevents concurrent *double-dispatch* of the effect; it does **not** by itself cover the
post-rollback failure-recording path, which ran in a later transaction, unlocked and without re-checking
state.

The README and the implementation now state the narrower, verified guarantee:

- the apply path claims rows with `FOR UPDATE SKIP LOCKED`;
- failure recording **re-claims** the row under a blocking `FOR UPDATE`;
- failure recording is a **no-op** when another drainer already set `processed_on_utc`.

## Reproduction (test-first, deterministic)

Pinned by
`InboxConcurrencyReliabilityTests.Failure_Recording_After_A_Concurrent_Success_Does_Not_Overwrite_The_Processed_Row`
(integration test against a real PostgreSQL).

The interleaving is **forced, not raced** against the scheduler. A failing attempt rolls back and records its
failure with no `await` gap in between, so the processor exposes a minimal internal test seam
(`AfterRollbackBeforeRecordFailureForTests`, a no-op in production) that fires *after* the rollback (lock
released) and *before* `RecordFailureAsync`:

1. Processor A claims the row and fails in dispatch → rolls back (lock released).
2. In the seam, processor B claims the freed row, applies the effect, and commits it `processed`.
3. A then runs `RecordFailureAsync` against the now-processed row.

Assertions: the effect is applied once, the row stays `processed` with `processed_on_utc` set, `retry_count`
stays `0`, and no `inbox_dead_letters` row exists.

After the fix the failure-recording is a no-op on an already-processed row, and the test is **green** on
`main`.

**Reproduce the red for reference.** Check out the pre-fix commit and run the test:

```bash
git checkout 6bd3018   # the intentionally-red commit
dotnet test src/Tests/ModulithReliabilityKit.IntegrationTests/ModulithReliabilityKit.IntegrationTests.csproj \
  --filter Failure_Recording_After_A_Concurrent_Success_Does_Not_Overwrite_The_Processed_Row
```

It fails with:

```text
Assert.Equal() Failure: Strings differ
Expected: "processed"
Actual:   "retrying"
```

Equivalently, on `main`, replace the `FOR UPDATE` re-claim in `RecordFailureAsync` with a plain
`FirstOrDefaultAsync(x => x.Id == id)` (the pre-fix read) and the same assertion fails.

## Fix

`RecordFailureAsync` re-claims the row in its own transaction with `SELECT ... FOR UPDATE` and, if
`processed_on_utc IS NOT NULL` (already succeeded), commits a **no-op**. Only a still-unprocessed row bumps
`retry_count` or moves to dead-letter. A **blocking** `FOR UPDATE` (not `SKIP LOCKED`) is deliberate: if a
concurrent drainer is still mid-apply, this waits for it to commit and then observes the now-processed row,
rather than skipping and recording a failure anyway. A DLQ uniqueness constraint (`(logical_id,
occurred_on_utc)` index + upsert/guard) is a defense-in-depth follow-up — it is *not* required to close this
race, since the source fix already prevents the spurious dead-letter insert.

## Lesson

A row lock only protects the critical section it wraps. A compensating / bookkeeping write that runs in a
*later, separate* transaction must re-establish the invariant — re-lock the row and re-check its state before
writing. Releasing a lock and then committing "what I decided earlier" is a classic lost update against
whoever ran in between.

## Next

- `09-lessons-learned/architecture-rules-for-my-own-project.md`
