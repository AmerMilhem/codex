# Trial Balance — Vertical Slice 2

Read-only Finance report sourced exclusively from posted journal entries and their persisted lines. No editable balances,
cached totals, projection, UI calculation, GL detail, AR/AP, Sales, Inventory, Manufacturing, FX, consolidation or analytics
store is introduced. Export is omitted to keep this slice bounded.

## Accounting and dates

Within one verified tenant/company's functional-currency book, for each account:

`OpeningNet = SUM(Debit - Credit WHERE PostingDate < StartDate)`

`PeriodDebit = SUM(Debit WHERE StartDate <= PostingDate <= EndDate)`

`PeriodCredit = SUM(Credit WHERE StartDate <= PostingDate <= EndDate)`

`ClosingNet = OpeningNet + PeriodDebit - PeriodCredit`

`OpeningDebit = max(OpeningNet, 0)`; `OpeningCredit = max(-OpeningNet, 0)`.

`ClosingDebit = max(ClosingNet, 0)`; `ClosingCredit = max(-ClosingNet, 0)`.

Movement columns are gross debits/credits, not net movement. Opening and closing columns split each account's net balance.
Grand totals sum those displayed columns, so gross period debit140 can coexist with net closing debit100.

Opening is **company-book lifetime before StartDate** (`openingScope=company-book-lifetime`), including previous fiscal years.
There is no implicit annual reset or fabricated retained-earnings carryforward. Any future fiscal-year closing is represented
by actual posted entries under a separately approved workflow. This matches the current ledger, which has no year-close
feature. Reporting is allowed across dates/fiscal periods and over open or closed periods. PostingDate, never CreatedAt or
PostedAt, determines inclusion. Only `status='Posted'` qualifies. Account status/posting permission now does not hide history.

An account appears when it has a nonzero opening net or any period debit/credit; an account with a zero closing balance but
period activity remains visible. Unused accounts or fully net-zero old accounts without period activity are omitted. Empty
results still return zero grand totals and successful reconciliation. No calendar coverage is required for an empty interval.

## Query, precision and snapshot

Finance.Application owns the read-query port and reconciliation rules. Finance.Infrastructure uses parameterized Npgsql SQL:
PostgreSQL filters/joins/groups ledger rows and calculates all per-account money columns. Only account aggregates are loaded
into memory. Existing company/status/posting-date and company/journal/account indexes are evaluated using actual plans.
No Dapper, index, schema migration or extra package is added without evidence. An equivalent EF projection is benchmarked.

One `REPEATABLE READ`, PostgreSQL-enforced `READ ONLY` transaction holds the book, account aggregation and independent
control queries to the same MVCC snapshot. This avoids false mismatches if posting commits between the queries. Transaction-
local verified tenant/company, request, correlation and trace settings match the Foundation isolation model; pooled settings
do not persist. No accounting locks, writer, SaveChanges, audit entry, outbox event or command receipt is invoked.

Line storage stays NUMERIC(24,6). PostgreSQL SUM remains exact NUMERIC. Results are transmitted as decimal strings using
`trim_scale(... )::text`; no numeric cast reduces their precision to a single journal's bound. Account totals can exceed a
single journal and eventually .NET decimal capacity, so application grand-total/reconciliation arithmetic uses bounded
six-place BigInteger scalars. It never aggregates in-memory journals. Angular displays server strings unchanged, with no
parseFloat/Number rounding, so Arabic/English show identical figures. No rounding or suspense/correction row is manufactured.

## Independent reconciliation

A second SQL query reads posted ledger facts independently of account-master joins and report row-omission rules. It
returns independently calculated opening/closing net-side totals, gross opening/closing ledger debit/credit, gross period
debit/credit, posted journal/line counts and the number of invalid journals. Per-journal checks prevent opposite errors from
cancelling globally. Both queries share the same read snapshot.

Application checks: opening/period/closing debit=credit; gross source-ledger balance; every displayed grand-total column
matches its independent control; each account satisfies the closing equation, nonnegative sides and net-side representation;
and invalid posted journal count is zero. Failure returns `reconciliation.passed=false` with stable check codes and exact
expected/actual values. The API still returns the authorized report so a warning cannot hide its figures. No repair occurs.
OTel outcome counter/duration histogram and a warning containing failed check codes make failures observable without logging
financial values or high-cardinality metric labels. Report timestamp identifies snapshot generation, not a financial posting.

## API and security

`GET /api/v1/tenants/{tenantId}/companies/{companyId}/finance/trial-balance?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD`

Requires independently verified tenant/company membership with `Finance.Journal.View`. Create/Edit/Post alone do not grant
access. Missing dates, invalid syntax or start>end fail400; valid same-day ranges are supported. A missing configured book
uses the existing FIN_BOOK_NOT_CONFIGURED error. Unauthorized scope fails403 before querying; missing route scope fails404.
Current permission is resolved for each request. RLS WITH CHECK/FORCE remains active; explicit SQL scope predicates help the
plan but do not replace RLS. No If-Match, Idempotency-Key or POST command is used for reporting.

Response includes scope/dates/currency/minorUnits/openingScope/generatedAt, account code/name and six amount strings,
grand totals, independent ledger controls and reconciliation checks. Malformed range uses
`FIN_REPORT_INVALID_RANGE / finance.report_invalid_range` in the existing Foundation error contract. Error prose is localized
by the client; code/errorId/requestId/correlationId/traceId remain stable.

## Web and local use

Run the existing README setup/API/Web steps. The verified local session now links to Trial balance from Workspace and the
journal screen. Choose dates and Run/Refresh. The report shows opening, gross period movement, closing, grand totals,
loading/empty/error states and reconciliation warning details. Date changes cancel obsolete requests and clear the prior
report. With the separately authorized General Ledger V1 slice, account codes link to read-only GL with the same company/account/dates. No accounting action is exposed. See [General Ledger](GENERAL-LEDGER-V1.md). The wide table scrolls within
the screen on mobile; Arabic/English choose RTL/LTR without changing wire dates or financial strings.

Verification: `bash scripts/verify.sh` retains every Foundation and Manual Journal regression and adds Trial Balance tests,
100,000-line measurements, and `scripts/test-trial-balance-e2e.py`. The browser runner uses its own disposable database and
real API to post golden fixtures, then verifies read-only reporting and unchanged database state with a closed fiscal period.
Test-only bulk performance setup keeps financial constraints/triggers enabled and has its own bounded setup timeout;
production report command timeout is unchanged. Preparation time is reported separately from query time.

The retained evidence under `docs/evidence/trial-balance-v1/` records results, SQL plans, comparison measurements and limits.
Local hot-cache timings are not a production capacity/SLA claim. No projection or extra index is inferred from synthetic data.
Production identity/deployment/legal gates from Foundation remain unchanged. Stop after the slice acceptance gate.
