# Manual Journal Reversal — V1

## Contract and boundary

`POST /api/v1/tenants/{tenant}/companies/{company}/finance/journals/{journalId}/reverse` is a business command requiring
`Finance.Journal.Reverse`, a current tenant/company membership, `If-Match`, `Idempotency-Key`, reversal posting date and a
nonblank reason of at most 1000 characters. `Finance.Journal.Post` does not grant reversal. The standard Foundation error envelope
returns stable Finance, authorization, concurrency and idempotency codes.

This slice reverses one Posted manual journal in full. It does not partially reverse lines, reverse a reversal, create chains,
schedule work, or add AR/AP, tax, inventory or approval behavior.

## Financial behavior

The command reloads and locks the original, verifies the expected revision, and rejects Draft, already-reversed and reversal
journals. It copies every persisted line in position order to a new journal with debit and credit exchanged exactly. Account,
currency, precision and descriptions remain the stored values; no amount is recalculated. The new posting date and reason are
independent. The reversal period must exist and be Open, even when the original period is Closed.

The new journal passes the ordinary posting validation and obtains its own concurrency-safe business number. Both journals remain
`Posted`. The original header, lines, number, date, revision and audit history are unchanged. One immutable relationship identifies
the original and reversal and carries the reversal date/reason, actor, request, correlation and timestamp.

Trial Balance and General Ledger require no reversal branch: the second journal is a normal Posted ledger entry. Reporting Period A
shows the original, Period B shows the opposite, and their combined net is zero while each period remains independently balanced.

## Atomicity, idempotency and concurrency

One PostgreSQL transaction performs lock/reload, period/account/balance validation, new journal persistence and numbering, posting,
relationship, audit, outbox and idempotency receipt. No event is dispatched before commit. A unique scoped original relation and a
unique scoped reversal ID make double reversal impossible at the database boundary. Concurrent attempts serialize on the original;
the winner commits one effect and the loser receives the stable already-reversed/concurrency outcome.

The command identity is `Finance.ReverseManualJournal`. A same-key, same-fingerprint retry returns the stored result after a lost
response. The same key with a changed date or reason returns `API_IDEMPOTENCY_CONFLICT`. A new key cannot create another reversal.

## Audit and integration

The transaction records `JournalReversalRequested`, `JournalReversed`, and the ordinary `JournalPosted` for the new journal.
Typed query enrichment exposes the relationship on both journal details and both reversal audit rows. The compact versioned event
`finance.journal-entry-reversed.v1` contains original ID, reversal ID/number, posting date and currency. The standard posted event is
also emitted for the new posted journal. The worker validates the reversal event against persisted state and records an
Inbox-deduplicated delivery trace; it creates no accounting effect.

## UI and errors

The Posted Journal screen shows Reverse only with the independent permission and only while no relationship exists. The user enters
date and reason and explicitly confirms creation of a separate immutable journal. During an uncertain response, the UI freezes the
same date/reason/key/revision for a safe retry. Once committed, both journal screens show the bidirectional relationship, reason and
date with navigation; there is no edit or second Reverse action. Arabic/English and RTL/LTR share the same financial values.

Stable slice errors are `FIN_JOURNAL_NOT_POSTED`, `FIN_JOURNAL_ALREADY_REVERSED`,
`FIN_JOURNAL_REVERSAL_NOT_ALLOWED`, `FIN_REVERSAL_PERIOD_CLOSED`, and `FIN_REVERSAL_DATE_INVALID`, alongside the existing
`DOC_CONCURRENCY_CONFLICT`, `API_IDEMPOTENCY_CONFLICT`, and `AUTH_SCOPE_DENIED` contracts.

## Verification

Run the complete regression gate:

```bash
source scripts/env.sh
export POC_CHROMIUM=/usr/bin/chromium
bash scripts/verify.sh
```

The retained evidence is in [Reversal acceptance](evidence/reversal-v1/ACCEPTANCE.md).
