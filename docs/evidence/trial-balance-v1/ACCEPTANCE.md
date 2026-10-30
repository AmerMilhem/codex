# Vertical Slice 2 — Trial Balance — Acceptance

**Gate: SLICE PASS** — 2026-10-18 UTC. Authorized scope: read-only Trial Balance inside Finance.
No production identity, deployment or pending legal/compliance decision is approved by this source/test gate.

## Implementation

Implemented a dedicated application query, exact PostgreSQL account aggregation, independent ledger controls/reconciliation,
authorized GET endpoint and Angular report. The only financial sources are persisted Posted journals/lines. Drafts, cached
balances and UI totals never contribute. No accounting write, audit entry, command receipt, financial event, editable balance,
projection, GL detail, Sales, Inventory, AR/AP, Manufacturing, FX, consolidation or reporting warehouse was added. Export is omitted.

API: `GET /api/v1/tenants/{tenantId}/companies/{companyId}/finance/trial-balance?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD`.
Requires Finance.Journal.View and current verified tenant/company membership. The report/control reads run in one
PostgreSQL-enforced READ ONLY, REPEATABLE READ transaction with transaction-local RLS scope and correlation settings.

## Tests

The final complete `bash scripts/verify.sh` run exited0: locked restore, dependency checks, Release build (0 warnings/errors),
format/type checks, all suites, both financial real-browser workflows, vulnerability scans and local API/Worker publish passed.

| Suite | Passed | New since Manual Journal |
|---|---:|---:|
| .NET Unit | 27 | 8 |
| Architecture | 9 | 0 |
| PostgreSQL/API Integration | 67 | 12 |
| Angular Unit | 19 | 8 |
| **Automated tests** | **122** | **28** |
| Shell browser acceptance checks | 9 | 0 |
| Manual Journal browser acceptance checks | 19 | 0 |
| Trial Balance browser acceptance checks | 22 | 22 |
| **Browser checks** | **50** | **22** |

Zero failed or skipped tests. xUnit theory rows and Vitest cases are counted as tests; named browser checks are separate.
SQL evidence values and assertions inside a test are not counted again. Every previous Foundation and Manual Journal
regression remains green, including posting, immutable history, period validation, RLS, idempotency, audit/outbox and migrations.
No currently reported NuGet/npm vulnerability was found. The CI workflow enforces this common entry point; remote execution
and deployment are not claimed, and no push was performed.

## Accounting

For each account, OpeningNet=sum(debit-credit) before StartDate; period debit/credit are gross sums between inclusive
StartDate/EndDate; ClosingNet=OpeningNet+PeriodDebit-PeriodCredit. Opening/closing debit=max(net,0), credit=max(-net,0).
Grand totals sum displayed columns. Opening includes all earlier posted history in the company book, including prior fiscal
years; there is no implicit year reset. Actual future year-close transactions require separate approval.

Golden scenario, posted Expense100/Cash100 then Cash40/Revenue40, plus excluded draft999:

| Account | Opening D/C | Period D/C | Closing D/C |
|---|---|---|---|
| Cash1000 | 0 / 0 | 40 / 100 | 0 / 60 |
| Revenue4000 | 0 / 0 | 0 / 40 | 0 / 40 |
| Expense5000 | 0 / 0 | 100 / 0 | 100 / 0 |
| **Grand totals** | **0 / 0** | **140 / 140** | **100 / 100** |

Adding February Expense20/Cash20 leaves January unchanged; February totals are opening100/100, movement20/20, closing120/120.
Adding prior-year December Expense10/Cash10 changes January to opening10/10, movement140/140, closing110/110.
Start-1, Start, End and End+1 boundaries, same-day ranges, historical PostingDate versus CreatedAt, empty ledgers, opening-only
accounts and net-zero closing balances are tested. Closed periods and now-inactive/non-postable accounts remain readable.
Many0.001 entries and large amounts aggregate exactly beyond a single journal's numeric bound. Six-place BigInteger checks
also verify values beyond .NET decimal capacity; PostgreSQL SUM is returned as exact decimal strings without casts/rounding.

## Reconciliation and immutability

An independent SQL control query, without account-master joins/report-row omission, verifies every grand-total column,
gross source-ledger balance and posted journal/line integrity. Per-account closing equations and net-side representation are
checked independently of the UI. All positive scenarios reconcile. Deliberately corrupting two disposable posted journals
in opposite directions leaves grand totals balanced but produces invalidJournalCount2 and a visible reconciliation failure.
The warning/log code is asserted; the report does not repair or hide the figures. Runtime cannot perform that test-only
superuser corruption. SQL immutability guards are restored before the report runs.

The browser closed-period refresh compares before/after digests of all Finance, Audit, Outbox, Inbox and Idempotency state:
**unchanged**. Report requests are GET-only. Independent final SQL finds posted journals4, draft1, posting outbox rows4,
posting receipts4, January debit140.000000/credit140.000000 and zero globally unbalanced journals. The four posting effects
belong to golden fixture creation; the report adds none. Unit/integration state hashes cover repeated reads too.

## Security

Tenant/company RLS and permission tests pass. An allowed user in Company A cannot request Company B or another tenant;
current authorization is checked before any query. Create/Edit/Post are not View permission. Missing/invalid date ranges
return400; anonymous requests401, unauthorized scopes403, and a missing context route404. A one-connection report pool
alternates tenants correctly and returns unscoped zero rows with transaction_read_only reset afterward. The existing runtime
role remains a nonowner/NOSUPERUSER/NOBYPASSRLS login. Production OIDC/MFA is still the prior separate gate.

## Performance and database

Measured on500 constrained posted journals, **100,000 lines**,100 active accounts;250 journals contribute opening and250
contribute period movement. Financial triggers/FKs/deferred commit checks stay enabled. Fixture preparation took66.400s and
is excluded from report timings. Five warmed local runs produced these medians:

| Strategy | Median ms |
|---|---:|
| Explicit Npgsql SQL account projection | 25.771 |
| Equivalent EF Core server projection | 28.279 |
| Complete report including independent controls | 175.201 |

The primary query returns100 account aggregates; its actual plan records101,321 scan tuple visits across tables/loops
(including filtered rows), not101,321 unique ledger lines. Broad-history queries must examine eligible ledger history for
opening. Selective-date EXPLAIN uses existing company/status/posting-date and company/journal-line indexes. Control and
selective plans are retained separately. SQL was selected for explicit control-query structure and exact NUMERIC-to-text
results, supported by these measurements; this is not a claim of universal superiority over EF.

**Indexes added0; migrations added0; package/technology versions changed0.** No random index or projection was added.
Local warm-cache single-client measurements are not a production capacity benchmark or SLA. Report timeout remains the
existing30s default; only constrained bulk fixture preparation has a bounded180s timeout.

## Web

Date range/Run-Refresh, eight account columns, grand totals and loading/empty/error/reconciliation-warning states work.
Arabic/English render identical exact strings, RTL/LTR and mobile-width containment pass, and date changes cancel stale
requests. There are no financial edit/post controls or GL drill-down links. Real browser assertions use the actual API and
database; additional independent acceptance reads inspect controls without mocking responses. Screenshots are retained.

## Deviations, environment corrections and blockers

Opening is explicitly company-book lifetime, with no annual reset or new year-end close workflow. Export is omitted as optional.
No business scope deviation or unresolved slice blocker remains. During the complete gate, a test-database startup race was
fixed by waiting for final TCP readiness instead of the temporary initialization Unix socket. Repeated disposable database
retention exhausted local disk; only stopped Testcontainers created by this task were removed, preserving development/PoC
databases. The fixture now auto-removes on stop and each browser runner deletes only its own anonymous test volumes.
No assertions were weakened, no ledger guards disabled for performance, and no broad Docker prune was used.

Production identity/MFA, deployment/operations and Jordan/JoFotara/retention/residency verification remain pending from
Foundation. Historical acceptance evidence is preserved. See [decisions](../../adr/TRIAL-BALANCE-V1.md) and
[contracts/formulas/local guide](../../TRIAL-BALANCE-V1.md).

## Evidence

- [Summary/counts](summary.json), [source hashes](source-manifest.json), [complete verification log](verification.txt).
- [Integration TRX](Erp.IntegrationTests.trx), [unit TRX](Erp.UnitTests.trx), [architecture TRX](Erp.ArchitectureTests.trx),
  [Angular unit results](angular-unit-results.json).
- [Trial Balance golden/SQL results](golden-results.json), [browser results/snapshots](browser-results.json),
  [Manual Journal regression](journal-regression-results.json), [Foundation browser regression](foundation-browser-results.json).
- [Performance samples](performance.json), [account plan](accounts-plan.json), [independent control plan](control-plan.json),
  [selective plan](selective-plan.json), [measured EF SQL](ef-projection.sql).
- [Arabic](trial-balance-ar.png), [English](trial-balance-en.png), [mobile RTL](trial-balance-mobile-ar.png),
  [NuGet audit](nuget-audit.json), [npm audit](npm-audit.json).

## Final Gate

**SLICE PASS**. Stop. Proposed next vertical slice: **General Ledger Detail — Read Only**, to trace an account's reported
balance back to immutable posted journal lines. It is a proposal requiring separate approval and has not started.
