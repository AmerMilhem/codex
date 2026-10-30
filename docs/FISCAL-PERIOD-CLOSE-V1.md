# Fiscal Period Close — V1

## Contract

Finance exposes a scoped period list, `GET /periods/{id}/pre-close`, and the business command
`POST /periods/{id}/close` under `/api/v1/tenants/{tenant}/companies/{company}/finance`.
The preview and command require `Finance.Period.Close`; the list requires `Finance.Period.View`.
The command requires `If-Match: "1"`, an `Idempotency-Key`, and an optional comment up to
1000 characters. Posting, reversing, or viewing journals does not grant close permission.
The standard error envelope includes `FIN_PERIOD_ALREADY_CLOSED`, `FIN_PERIOD_CLOSE_NOT_ALLOWED`,
`FIN_PERIOD_RECONCILIATION_FAILED`, `FIN_PERIOD_CONCURRENCY_CONFLICT`, and
`FIN_PERIOD_INVALID_STATE`, plus Foundation scope/idempotency errors.

## Financial rule

An Open period at revision 1 can become Closed at revision 2 once. Pre-close reads the
posted ledger for the period, independently recomputes Trial Balance totals and ledger
control, compares each account's period debit/credit with a separate posted-line GL scan,
and checks that every posted journal dated in the period refers to that period. Any failed
check blocks close. The preview writes nothing and is advisory: the command locks and
reloads the period and repeats all checks inside its own transaction. Closed-period TB and
GL remain readable, and closing does not modify any journal or line. A later Post or
Reversal dated into the period is rejected by the existing posting rules.
AR/AP, Inventory and other future subledgers have no implemented close checks and are
not represented as passing checks in the preview.

Period definitions cannot overlap in the same company; a book-row lock serializes
insertion and edits. A period containing posted journals cannot be redefined. Close
metadata, actor, comment and reconciliation evidence are immutable once Closed.
`closed_at` uses PostgreSQL microsecond precision. A prior database's already Closed
records are marked `migration/legacy-closed-period` with `legacy:true,passed:false`;
the migration does not invent a verified close actor or reconciliation result.

## Atomic effects and delivery

The single local transaction persists the Closed state and evidence, two audit actions
(`FiscalPeriodCloseRequested`, `FiscalPeriodClosed`), a compact
`finance.fiscal-period-closed.v1` outbox event, and the idempotency receipt. A deferred
database guard requires those audit/outbox effects before commit. No event is sent
before commit. A worker validates the versioned event against persisted period state
and records an Inbox-deduplicated delivery trace without changing finances.
A same-key retry after a lost response returns the stored receipt; a changed payload
with that key conflicts. Different-key concurrent closes leave exactly one Closed state.

The Angular period list displays dates, status and close actor/time in Arabic or English.
The Open-period action shows the pre-close checks and requires explicit confirmation.
After an uncertain response the form freezes comment, revision and key for a safe retry.
Closed periods show no close action.

## Verification

Run `source scripts/env.sh`, set `POC_CHROMIUM=/usr/bin/chromium`, then run
`bash scripts/verify.sh`. The full gate includes earlier slices and
`scripts/test-period-close-e2e.py`, which uses a disposable PostgreSQL database and
real Angular, HTTP and worker-compatible infrastructure. Retained results are in
`artifacts/period-close/` and [acceptance evidence](evidence/period-close-v1/ACCEPTANCE.md).
