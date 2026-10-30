# General Ledger Detail — V1 acceptance

**Final Gate: SLICE PASS**

Recorded 2026-10-20 (Asia/Amman). Scope: user-authorized Finance read-only General Ledger Detail. No next slice, remote push/CI execution, deployment or compliance approval performed.

## Implementation

Dedicated Application query/reader port, parameterized Npgsql read adapter, actual PostgreSQL window pagination, GET contract, Angular report, Trial Balance → GL → existing Posted Journal/audit navigation. Verified Tenant/Company/View context and FORCE RLS. PostgreSQL enforces READ ONLY inside one REPEATABLE READ transaction covering all report reads. No new financial table, projection, cache or mutable account balance.

Source/design: [guide](../../GENERAL-LEDGER-V1.md), [ADR](../../adr/GENERAL-LEDGER-V1.md), [query/validation/reconciliation](../../../src/Modules/Finance/Erp.Finance.Application/GeneralLedgerQuery.cs), [SQL](../../../src/Modules/Finance/Erp.Finance.Infrastructure/GeneralLedgerSql.cs), [reader](../../../src/Modules/Finance/Erp.Finance.Infrastructure/GeneralLedgerReader.cs).

## Tests

Final `bash scripts/verify.sh` exit **0**. [Full verification log](verification.txt).

| Suite | Passed | New |
|---|---:|---:|
| .NET unit | 37 | 10 |
| Architecture | 9 | 0 |
| PostgreSQL/HTTP integration | 77 | 10 |
| Angular | 28 | 9 |
| **Automated tests** | **151** | **29** |

Zero failures/skips. Separate named real-browser checks: Foundation 9 + Manual Journal 19 + Trial Balance 22 + General Ledger 24 = **74**, of which **24 new**. SQL assertions are not counted again. xUnit theory rows are counted by the test runner.

Retained: [machine-readable summary](summary.json), [.NET unit TRX](Erp.UnitTests.trx), [architecture TRX](Erp.ArchitectureTests.trx), [integration TRX](Erp.IntegrationTests.trx), [Angular results](angular-unit-results.json), [Foundation browser](foundation-browser-results.json), [Manual Journal regression](journal-regression-results.json), [Trial Balance regression](trial-balance-regression-results.json), [GL golden browser/database controls](golden-results.json).

All Foundation, Manual Journal and Trial Balance regressions remain green, including posting invariants, RLS, concurrency, idempotency, rollback, outbox recovery/inbox, audit, migrations/upgrade and architecture boundaries. Previous Trial Balance assertions that forbade GL links now explicitly allow/verify the newly authorized **read-only** account links; the ban on accounting mutations remains asserted.

## Accounting

Real API-created golden fixture:

| Cash | Debit | Credit | Net balance |
|---|---:|---:|---:|
| Prior-period opening | 100 | 0 | 100 |
| Start date movement | 40 | 0 | 140 |
| End date movement | 0 | 25 | 115 |
| Closing | | | **115 debit** |

Draft 999 is excluded; after-end posted 800 is excluded. Trial Balance cash opening/period/closing equals `100 / 40 / 25 / 115`. PostingDate drives selection, independent of creation timestamp. Same-date journals use UUID order; repeated account lines in one journal use position. Closed periods and inactive historical accounts remain readable. Empty accounts return zero; opening-only credit remains signed `-10` without fake movements.

Many small amounts plus large values accumulate exactly to **2000000000000000000.018**, including across the last-page boundary. SQL NUMERIC + decimal strings + scaled integer checks; no monetary float/double or UI recomputation.

## Reconciliation

GL independently queries the actual Trial Balance account projection within the same snapshot. Opening, gross debit, gross credit and closing match exactly. Running increments and first/last page anchors are checked. Unit cases deliberately inject each total mismatch and a wrong running increment; each returns failure with stable check codes. Failures remain visible/observable; no repair.

Browser-run database controls: 4 posted journals, 1 draft, 4 posting outbox rows and 4 idempotency receipts; January global debit=credit=65.000000; zero unbalanced journals. After closing the January period, report navigation and refresh left a digest over all Finance state + audit/outbox/inbox/idempotency **unchanged**. Worker is not running in this read-only check.

## Security

Runtime tenant/company scope, unknown/foreign account indistinguishability, modified identifiers, missing permission, malformed/missing/inverted dates, invalid pagination and missing route context all tested. Cross-company/tenant routes return 403 before financial reads; foreign account IDs in an authorized company return 404. Create permission does not grant View. Pool-size-one alternating-tenant tests show no leaked rows/context; transaction_read_only resets to off after disposal.

The existing immutable Posted Journal screen supplies actor/time/request/correlation audit details. Report requests and drill-down use GET only. NuGet and npm scans report no vulnerabilities at verification time: [NuGet](nuget-audit.json), [npm](npm-audit.json).

## Pagination

A 13-movement same-date fixture is paged with limits **1, 3, 5, 13, 200**. Concatenated rows equal the full result, line IDs/order/running amounts are identical, each page's opening equals the preceding page's last running balance, and full-period totals/closing do not change. Beyond-last-page is empty with the same closing balance. A same-journal repeated-account test checks position ordering.

Performance fixture: middle page offset13,000/limit50 has page opening38, first running38.001 and last38.05. Last page offset25,999/limit1 closes at51. Browser confirms both 1-row pages equal the 200-row result and navigation preserves dates/account/page.

Stable pagination refers to identical posted source data. Each request sees a fresh committed snapshot; newly posted/backdated journals can change later requests. UI and contract document live-report semantics. There is no durable cross-request as-of snapshot or long-held transaction.

## Performance

**102,000 journal lines, 510 posted journals; one account has 51,000 lines (25,000 opening + 26,000 period)**. Fixtures retain all schema/financial guards. Five warm runs, local single client, no SLA claim.

| Measured segment | Median ms |
|---|---:|
| Opening SQL | 11.0976 |
| Count + period aggregate SQL | 9.9679 |
| Movement SQL, middle page | 25.7414 |
| Independent TB account reconciliation SQL | 16.7626 |
| Full report reader, transaction/metadata/all controls | **63.7054** |

Full-report timing is the backend reader including transaction/pool/context and control queries, not network/browser latency. Real HTTP and browser flow are separately exercised by the acceptance runner.

Plans show the existing `IX_journal_lines_tenant_id_company_id_account_id` bitmap path for scoped opening/count/movements; window sorting and prefix accumulation precede LIMIT/OFFSET. Broad-history TB reconciliation can choose parallel scans. 50 movement rows returned from the requested 26k-row period. No observed need justified a new index. OFFSET and lifetime opening still cost eligible-history work; this is not constant-time pagination.

Evidence: [raw measurements](performance.json), [scan/row summary](plan-summary.json), [opening plan](opening-plan.json), [count/period plan](count-and-period-plan.json), [movement plan](movements-middle-page-plan.json), [TB reconciliation plan](trial-reconciliation-plan.json). Scan tuple visits count visits across scan nodes/loops/relations, not distinct financial lines.

## Web

Arabic/English exact same financial values, RTL/LTR, account/date/limit selectors, Refresh, opening/full-period/closing totals, paginated running amounts, empty/loading/error/reconciliation-warning states. Browser verifies Trial Balance → Cash GL → existing Posted Journal with audit → GL return. No financial mutation buttons. Stale requests cancel when filters change.

Screenshots inspected: [Arabic](general-ledger-ar.png), [English](general-ledger-en.png), [Arabic mobile](general-ledger-mobile-ar.png). Wide table scrolls inside the page on mobile.

## Database

**No migration, no new index, no financial table.** Existing scope integrity/FKs/precision/posted-state guards/RLS remain unchanged. Migration clean-install and upgrade regression tests pass. Query plan evidence supports existing account index; no speculative write/storage overhead added.

## CI

The existing workflow executes `scripts/verify.sh`, now including GL browser acceptance. It enforces locked package compatibility, Release build, formatting, all tests, security scans and API/Worker local publish. Workflow uploads `artifacts/` as Finance evidence. Remote CI was **not run**; this record is local execution of its same gate.

## Deviations

No functional scope deviation. No optional export, journal-number/reference filter, new reporting module, or unrelated domain feature. Explicit signed balances are the documented equivalent of debit/credit balance sides. Live snapshot-per-request semantics and bounded offset pagination are documented design choices. Historical baseline acceptance records remain unchanged.

## Blockers

None within this slice. Existing production identity/MFA, deployment/operations, Jordan/JoFotara/legal/retention/residency gates remain pending separately. No production readiness or compliance approval is implied.

## Evidence integrity and next gate

[Source SHA-256 manifest](source-manifest.json) records reviewable source/docs/config. Summary also retains TRX hashes. No commit, push or deployment was performed.

Proposed next slice only: **Manual Journal Reversal**. It would allow a traceable correction of a posted journal using a separately balanced reversing entry in an allowed period, while preserving original history, with TB/GL proving the effect. **Not started; separate user approval required.**
