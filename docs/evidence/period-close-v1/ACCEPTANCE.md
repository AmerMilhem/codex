# Fiscal Period Close — V1 acceptance

**Final Gate: SLICE PASS**

Recorded 2026-10-23 UTC. Scope: user-authorized Finance Fiscal Period Close. No subsequent slice, remote push, deployment or compliance approval was performed.

## Implementation

Finance now exposes a scoped period list, read-only pre-close reconciliation, and an explicit `CloseFiscalPeriod` command. An independent permission, expected revision, idempotency key and period-row lock control Open→Closed. The command persists actor/time/comment and verified checks, with two audit actions, one versioned outbox event and receipt in the same transaction. An Inbox-deduplicated worker traces event delivery. The Angular list and close flow support Arabic/English and RTL/LTR. See the [guide](../../FISCAL-PERIOD-CLOSE-V1.md), [ADR](../../adr/FISCAL-PERIOD-CLOSE-V1.md), [command](../../../src/Modules/Finance/Erp.Finance.Application/CloseFiscalPeriodCommand.cs) and [database guard](../../../src/Modules/Finance/Erp.Finance.Infrastructure/PeriodCloseSecuritySql.cs).

## Tests

`bash scripts/verify.sh` exit **0**. Release build has zero warnings/errors; dependency, format, Angular build, security audit, publish and all previous slice regressions passed. A final rebuild/format and the 12 close integration tests passed after the migration downgrade guard edit.

| Suite | Passed | New since Reversal V1 |
|---|---:|---:|
| .NET unit | 51 | 6 |
| Architecture | 9 | 0 |
| PostgreSQL/HTTP integration | 107 | 12 |
| Angular | 41 | 5 |
| **Automated tests** | **208** | **23** |

Zero failures/skips. Named real-browser checks total **112**, including **16 new** close checks. SQL assertions are not counted again. Retained: [summary](summary.json), [unit TRX](Erp.UnitTests.trx), [architecture TRX](Erp.ArchitectureTests.trx), [integration TRX](Erp.IntegrationTests.trx), [Angular results](angular-unit-results.json), and [golden result](golden-results.json).

## Close Integrity

The pre-close preview checks Open state, posted-ledger controls, exact Trial Balance balance, per-account GL movement, and posted-period mapping. The command repeats the checks under a period lock; a known unbalanced posted journal blocks close with `FIN_PERIOD_RECONCILIATION_FAILED` and leaves the period Open. Direct status-only updates, overlapping definitions and Closed metadata edits fail database guards. The golden period moves from `Open:1` to `Closed:2` exactly once.

## Financial Integrity

The golden Posted journal remains Debit Expense 100 / Credit Cash 100. Independent SQL finds zero unbalanced journals. No posted journal or line is rewritten. The period's TB still has debit 100 and credit 100 and the Expense GL still has its original movement after close.

## Posting Protection

Post and Reversal dated in the Closed period return their stable closed-period business errors. An independently Open period still permits posting; the close does not freeze other periods.

## Reliability

Six concurrent same-key attempts share one result; different-key attempts yield one winner. A deliberately lost browser response after the real commit is retried with the identical key/comment/revision and produces one receipt and one effect. A changed payload conflicts. An injected post-write failure rolls back status, metadata, audit, outbox and receipt.

## Security

`Finance.Period.Close` is independent from journal View/Post/Reverse; the period list uses `Finance.Period.View`. Tenant and company are verified context plus FORCE RLS, not trusted route parameters. Cross-tenant/company attempts are rejected. The golden database confirms `relforcerowsecurity=true` after migration. [NuGet](nuget-audit.json) and [npm](npm-audit.json) scans reported no vulnerabilities at execution time.

## Reporting

Trial Balance and General Ledger remain read-only and available for Closed periods, with the same posted-ledger numbers and reconciliation result before and after close. No balance projection or special report adjustment was added.

## Audit & Outbox

Exactly one `FiscalPeriodCloseRequested`, one `FiscalPeriodClosed`, one `finance.fiscal-period-closed.v1` event and one close idempotency receipt exist in the golden database. Actor, tenant/company, request, correlation and timestamp are retained in audit. The compact event omits journal details. Worker delivery is Inbox-deduplicated and creates no financial effect.

## Database

Migration `20261023152528_FiscalPeriodCloseV1` extends only `finance.fiscal_periods` with revision and close metadata/evidence; no table or new index is added. A check and triggers enforce valid transition, immutable Closed rows, no overlapping company periods, and matching audit/outbox at commit. Runtime has narrow column-level UPDATE permission. Clean install and upgrade from the previous Finance schema passed; previously Closed rows are marked explicitly as unverified legacy closes. The all-tenant backfill temporarily removes FORCE RLS inside the migration transaction and restores it before commit.

## Web

Actual Angular/HTTP/PostgreSQL browser acceptance covers list, pre-close result, explicit confirmation, lost-response retry, Closed action removal, Arabic RTL and English LTR. [Arabic screenshot](period-close-ar.png) and [English screenshot](period-close-en.png) retain the rendered views.

## Deviations

No functional scope deviation. The migration uses a temporary, transactional `NO FORCE` window for the migration owner to backfill all tenant rows; FORCE is restored before commit and verified. Legacy closes cannot be assigned a verified historical actor or reconciliation result, so they are marked unverified. The period list is capped at 500 rows as a bounded V1 read; pagination is a later enhancement.

## Blockers

None within this slice. Future subledger checks (AR/AP/Inventory), period reopening, year-end processing and fiscal-calendar administration remain outside scope. Production identity, deployment, Jordan/JoFotara and legal/retention/residency decisions remain separate gates.

## Evidence

[Machine summary](summary.json), [source SHA-256 manifest](source-manifest.json), [full verification log](verification.txt), [browser result](browser-results.json), and [regression results](reversal-regression-results.json). CI is wired to `scripts/verify.sh`; remote CI was not executed because no push occurred.
The last migration-guard change has separate [build](final-build.txt), [format](final-format.txt) and [12 close integration tests](final-close-tests.txt) logs.

## Final Gate

**SLICE PASS**

Proposed next slice only: **Fiscal Period Setup — controlled creation**. It replaces direct fixture-level period seeding with an authorized command and UI while reusing the overlap and scope rules. It has not been started and requires separate approval.
