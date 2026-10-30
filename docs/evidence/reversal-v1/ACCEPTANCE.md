# Manual Journal Reversal — V1 acceptance

**Final Gate: SLICE PASS**

Recorded 2026-10-21 (Asia/Amman). Scope: user-authorized Finance Manual Journal Reversal. No next slice, remote push/CI execution, deployment or compliance approval was performed.

## Implementation

Independent `ReverseJournalCommand`, exact opposite Domain construction, separate Posted journal and number, immutable bidirectional
relationship, authorized HTTP command, Angular confirmation/retry/view flow, and natural Trial Balance/General Ledger effects.
Original journal status, revision, posting date, number and lines remain unchanged. Source/design: [guide](../../MANUAL-JOURNAL-REVERSAL-V1.md),
[ADR](../../adr/MANUAL-JOURNAL-REVERSAL-V1.md), [domain](../../../src/Modules/Finance/Erp.Finance.Domain/JournalReversal.cs),
[command](../../../src/Modules/Finance/Erp.Finance.Application/ReverseJournalCommand.cs), and
[database guards](../../../src/Modules/Finance/Erp.Finance.Infrastructure/ReversalSecuritySql.cs).

## Tests

Final `bash scripts/verify.sh` exit **0**. [Full verification log](verification.txt).

| Suite | Passed | New since GL V1 |
|---|---:|---:|
| .NET unit | 45 | 8 |
| Architecture | 9 | 0 |
| PostgreSQL/HTTP integration | 95 | 18 |
| Angular | 36 | 8 |
| **Automated tests** | **185** | **34** |

Zero failures/skips. Separate named real-browser checks: Foundation 9 + Manual Journal 19 + Trial Balance 22 + General Ledger 24
+ Reversal 22 = **96**, of which **22 new**. SQL assertions are not counted again. Retained: [summary](summary.json),
[unit TRX](Erp.UnitTests.trx), [architecture TRX](Erp.ArchitectureTests.trx), [integration TRX](Erp.IntegrationTests.trx),
[Angular results](angular-unit-results.json), and [golden browser/database controls](golden-results.json).

All Foundation, Manual Journal, Trial Balance and General Ledger regressions remain green. Build completed with zero warnings/errors;
format/type checks, clean migration and N→N+1 upgrade, vulnerability gates, API publish and Worker publish passed.

## Financial Integrity

The reversal copies every stored line and swaps debit/credit exactly using decimal(24,6); no float/double or recomputation. Golden
original is Debit Expense 100 / Credit Cash 100. The new reversal is Debit Cash 100 / Credit Expense 100 with its own ID and
`MJ-2026-000002`. Both are individually balanced and immutable; independent SQL found zero unbalanced Posted journals.

## Period Handling

Original `MJ-2026-000001` remains dated 2026-01-01 in its subsequently Closed period. Reversal posts on 2026-02-01 in an Open
period. Closed/missing reversal periods return `FIN_REVERSAL_PERIOD_CLOSED` / `FIN_REVERSAL_DATE_INVALID`. Reporting can still read
the Closed original period. The command never reopens a period or backdates/mutates history.

## Reliability

One transaction performs original lock/reload, period/account validation, exact inverse creation, numbering, posting, relation,
audit, outbox and receipt. Concurrent attempts produce one relationship/effect. The browser deliberately aborts the response after
the real commit, then retries the frozen request/key and receives the one stored result. Same key + changed payload conflicts; new
key double reversal is rejected. Injected rollback leaves no partial journal, link, audit, outbox or receipt.

## Security

`Finance.Journal.Reverse` is independent: View/Post/Create/Edit do not grant it. Tenant and company are verified context and FORCE
RLS scope, not trusted route filters. Cross-tenant, cross-company and unauthorized attempts are rejected; pool reuse does not leak
context. Runtime has SELECT/INSERT only on the relationship and cannot UPDATE/DELETE it. [NuGet](nuget-audit.json) and
[npm](npm-audit.json) reported zero vulnerabilities at execution time.

## Reconciliation

Period A contains only the original; Period B contains only the opposite. Each Trial Balance period has equal debit/credit, and the
combined Expense and Cash closing balances are zero. Cash General Ledger moves 100 then -100 to closing zero and links to both
journal numbers. The report readers remain read-only and required no reversal-specific financial adjustment.

## Web

The Reverse action appears only for an eligible Posted original with permission. Date, reason and explicit immutable-journal
confirmation are mandatory. Unknown-response retry freezes date/reason/key/revision. After commit, both journal details show the
relationship, reason/date and navigation; fields remain read-only and the action disappears. Actual browser checks cover
Arabic/English and RTL/LTR with identical values. Inspected screenshots: [Arabic](reversal-ar.png), [English](reversal-en.png).

## Database

Migration `20261020202816_ManualJournalReversalV1` adds one purpose-specific table and one explicit scoped unique index plus its
scoped PK/FKs/checks. FORCE RLS, immutable trigger, chain prevention and deferred financial/evidence constraint are active. Clean
install, prior Finance schema upgrade with retained history, repeatability, snapshot and runtime readiness pass.

## Audit & Outbox

Exactly one `JournalReversalRequested`, one `JournalReversed`, one `finance.journal-entry-reversed.v1`, one reversal idempotency
receipt, and the ordinary JournalPosted/posting event for the new journal exist. Typed audit/detail views retain original/reversal
IDs, reason/date, actor, tenant/company, request, correlation and timestamp. Worker delivery uses Inbox deduplication and adds only
an operational trace.

## Deviations

No functional scope deviation. The security gate added an exact transitive Vite 8.3.5 override after advisories affected the
8.3.2 resolution; Angular remains 22.2.3, and the complete compatibility/build/browser gate passed with a zero-finding audit.

## Blockers

None within this slice. Production identity/MFA, deployment/operations and Jordan/JoFotara/legal/retention/residency decisions
remain external gates. Partial reversal, reversal chains and wider correction workflows remain outside the approved scope.

## Evidence

[Machine summary](summary.json), [source SHA-256 manifest](source-manifest.json), [full verification](verification.txt),
[browser result](browser-results.json), [Foundation browser](foundation-browser-results.json),
[Manual Journal regression](journal-regression-results.json), [Trial Balance regression](trial-balance-regression-results.json),
and [General Ledger regression](general-ledger-regression-results.json). No commit, push or deployment was performed.

## Final Gate

**SLICE PASS**

Proposed next slice only: **Fiscal Period Close — controlled command**. It is the smallest next step that replaces fixture-level
period closure with authorized, audited, concurrency-safe business behavior while reusing Posting, Reversal, Trial Balance and GL
controls. It has not been started and requires separate approval.
