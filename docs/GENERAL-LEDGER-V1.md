# General Ledger Detail — V1

Approved implementation scope: Finance-only, read-only interpretation of posted journal lines. No additional ledger, cache, balance field, financial writer, export, or future-module persistence dependency is introduced.

## Contract and authorization

`GET /api/v1/tenants/{tenantId}/companies/{companyId}/finance/general-ledger?accountId={id}&startDate=2026-01-01&endDate=2026-01-31&offset=0&limit=50`

Requires authenticated membership in the route tenant/company and **Finance.Journal.View**. Create/Edit/Post do not imply View. Account lookup uses both explicit scope predicates and runtime FORCE RLS, including inactive accounts with historical postings. Unknown and foreign accounts receive the same `RESOURCE_NOT_FOUND` contract (404). Invalid/missing account and invalid pagination use Foundation validation (400); missing/inverted dates use `FIN_REPORT_INVALID_RANGE` (400). Malformed typed parameters are rejected by the HTTP pipeline. Missing context has no routable endpoint. Foreign tenant/company or missing permission is 403.

Offset defaults to 0, must be nonnegative; limit defaults to 50 and is bounded to 1–200. Return account metadata, currency/minor units, dates, generation timestamp, opening scope, period totals, count, requested pagination, page opening, and only the requested movement rows. Monetary JSON fields are decimal **strings**. A row contains line/journal technical IDs, line position, PostingDate, business journal number, reference, journal and line descriptions, debit, credit and running balance.

## Accounting semantics

- Source: `finance.journal_entries.status='Posted'` joined to its own scoped `finance.journal_lines` only. Drafts never participate. Period status does not restrict reporting.
- Opening net = SUM(debit − credit) for PostingDate **before** StartDate, over the entire company book history (same `company-book-lifetime` semantics as Trial Balance).
- Period debit/credit = gross sums for StartDate **through** EndDate, inclusive.
- Running net at row n = Opening + SUM(debit − credit) over all ordered period rows up to n, including all preceding pages.
- Closing net = Opening + PeriodDebit − PeriodCredit.
- Positive net means debit; negative net means credit; zero is neutral. The UI explains the signs. It renders server numbers without floating-point monetary arithmetic.
- Order: **PostingDate ASC, JournalId ASC (PostgreSQL UUID order), Position ASC, LineId ASC**. Journal IDs are stable technical tie-breakers, not posting sequence/time. Unique LineId makes the complete ordering explicit. SQL `ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW` handles same-date/same-journal rows individually.
- PostgreSQL unconstrained NUMERIC aggregation/windowing retains persisted NUMERIC(24,6) precision. Exact scaled BigInteger logic reconciles decimal strings beyond individual-line numeric bounds; no decimal narrowing or rounded display calculations.

## Read model and pagination

`GeneralLedgerQuery` is the Application entry point, `IGeneralLedgerReader` its port, and Finance Infrastructure owns parameterized Npgsql SQL. PostgreSQL computes aggregates and windows; the application only loads account metadata and at most 200 movement rows.

One **REPEATABLE READ, READ ONLY** transaction covers verified scope GUCs, account/book lookup, opening, count/period sums, paged window and independent Trial Balance projection. RLS and session scope are transaction-local and reset on pooled reuse. There is no writer/UoW command, financial audit append, idempotency receipt, outbox event, or balance update in this reader. The only effects are operational tracing, duration metrics and reconciliation warning logs.

SQL computes the window **before** LIMIT/OFFSET. `pageOpeningBalance` is the balance immediately before the first returned row; an empty/out-of-range page reports closing. Period totals and closing cover the entire period, never just the page. OFFSET scanning is intentional and measured; no historical journal objects are materialized in application memory.

Each HTTP request uses a fresh database snapshot. This V1 is a live report, not a durable exported/as-of snapshot spanning multiple clicks. A new or backdated posting between page requests can change count/order/balances; refresh re-runs the current selection. The UI explains this. Pagination invariance means identical source data, scope, account and dates produce the same line balances at every page size. No long-lived database transaction or client snapshot cache is created.

## Reconciliation

The reader executes the actual existing `TrialBalanceSql.Accounts` projection for the selected account inside the same snapshot. This projection is independent of the GL opening/summary/window SQL. Its absence represents a zero account under Trial Balance's existing zero-row omission rule.

Stable checks compare opening net, period debit, period credit and closing net (`GL_TB_*`), check the closing equation and each visible running increment; first/last pages also reject mismatched opening/final running anchors. Failed checks expose expected/actual exact amounts and `passed=false`, remain visible in Angular and produce a warning/telemetry outcome. They never repair, hide or add suspense. GL is an account reconciliation to Trial Balance, not a replacement for Trial Balance's whole-ledger/per-journal integrity controls.

## UI and traceability

Trial Balance account links carry company/account/start/end to the lazy GL screen. Account/date/page-size selection, Run/Refresh, next/previous pages, Arabic/English, RTL/LTR, loading/empty/error/reconciliation-warning states are implemented. Changes cancel stale requests and clear prior results.

Journal number links open the **existing** Posted Journal detail screen with its audit actor/request/correlation/time. A return link restores GL account, dates, offset and limit. Posted fields remain immutable and no edit/post controls exist in reports. Returning to Trial Balance preserves dates. No separate journal screen or GL editing path was added.

## Verification and performance

Run the complete gate:

```bash
source scripts/env.sh
# If using the environment's existing Chromium instead of Playwright-managed Chromium:
export POC_CHROMIUM=/usr/bin/chromium
bash scripts/verify.sh
```

`GeneralLedgerTests` integration cases exercise the 100 → 140 → 115 golden sequence, all date boundaries, drafts, account filtering, same-date and same-journal position ordering, first/middle/last pages and sizes 1/3/5/13/200, out-of-range pages, zero/opening-only accounts, closed periods, many small/large values, HTTP authorization/validation, pool reuse and full persisted-state digest invariance. Unit tests inject reconciliation mismatches. Angular tests cover report states and navigation. The real browser runner uses actual API posting and PostgreSQL, then independently checks closed-period read-only state digests.

`GeneralLedgerPerformanceTests` creates **102,000 lines / 510 posted journals**, concentrating **51,000 lines in one account** (25,000 opening + 26,000 period). Measures opening, count/period, middle-page window at offset 13,000/limit 50, independent Trial Balance reconciliation, and full report, using five warmed runs. A final-page request verifies exact accumulated 51. All posting integrity/FK/RLS schema guards remain enabled during fixture creation; fixtures are disposable.

Plans and final measurements are retained under [evidence/general-ledger-v1](evidence/general-ledger-v1/ACCEPTANCE.md). Existing `(tenant_id,company_id,account_id)` index supports busy-account scans; the window sorts eligible period rows and computes the prefix before returning the page. Lifetime opening and broad report queries legitimately read history. No index/migration added without an observed bottleneck. Measurements are local warm-cache single-client observations, not production capacity or an SLA.

## Boundaries and remaining gates

No AR/AP, Sales, Inventory, Manufacturing, cost/profit centers, multi-currency, consolidation, intercompany, general Reporting module, or export. No technology/production authentication/legal gates are advanced. No next slice starts automatically. Remote CI/deployment requires its own execution; local `verify.sh` is the CI entry point.
