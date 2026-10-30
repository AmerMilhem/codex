# Fiscal Period Setup — acceptance record

**Gate: SLICE PASS.** The complete `scripts/verify.sh` run completed on 2026-10-24.
Its captured output is [verification.txt](verification.txt), and machine-readable counts,
hashes and database assertions are in [summary.json](summary.json).

## Complete regression

The final run passed 55 .NET unit, 9 architecture, 118 PostgreSQL integration and 44
Angular tests: **226 automated tests**, all passed, none skipped or failed. This adds
18 tests to the accepted Fiscal Period Close baseline. The Foundation, Manual Journal,
Trial Balance, General Ledger, Reversal, Close and Setup browser runners passed **136
named checks** in total, including 24 new Setup checks. Build, format, dependency
boundaries, NuGet/npm vulnerability checks and API/worker publish passed. These are
local CI-equivalent results; remote CI and production deployment were not executed.

The [TRX reports](Erp.IntegrationTests.trx) and
[Angular result](angular-unit-results.json) retain the test results. Individual browser
results are retained for [Foundation](foundation-browser-results.json),
[Journal](journal-regression-results.json), [Trial Balance](trial-balance-regression-results.json),
[GL](general-ledger-regression-results.json), [Reversal](reversal-regression-results.json),
[Close](period-close-regression-results.json) and [Setup](golden-results.json).

## Period integrity and reliability

Application and database checks reject exact duplicate, mid-range and inclusive-boundary
overlap. Concurrent overlap cannot both commit because creation holds the scoped book-row
lock. Identical concurrent requests and a retry after a committed but lost response create
one Open period with one audit pair, one outbox event and one idempotency receipt. Reusing a
key with different dates conflicts. Invalid input and failed transactions leave no partial
period or side effects. Tenant, company, independent Create permission and forced RLS checks
passed, including an unauthorized API request from the browser scenario.

## Migration integrity and retained history

The integration suite asserted the exact order of four Finance migrations: Manual Journal,
Reversal, Fiscal Period Close, then Fiscal Period Setup. A disposable clean database migrated
to latest with no pending model changes; expected columns, constraints, lock function and
FORCE RLS were present. A second database advanced one migration at a time through Close,
then Setup. It held Open and Closed periods and a balanced Posted journal linked to the
Closed period. A pre/post digest of historical period, journal and line fields matched;
Open/Closed status, dates, Close actor/time/comment/reconciliation and financial links were
preserved. New creator fields stayed null on both legacy periods, so no historical actor
was invented. See [migration incident](MIGRATION-INCIDENT.md) and
[hash/order check](migration-incident-check.json).

## Browser golden scenario

The disposable company began with **zero SQL-seeded fiscal periods**. Arabic RTL UI created
one Open period, retried the same request after simulated response loss, and displayed it.
The UI then validated and posted a balanced Manual Journal, reversed it in that Open period,
read Trial Balance and GL, closed the period through the existing Close UI, observed a later
Post rejection, and reread reports. The posted journal and reversal remained balanced;
Trial Balance period debit/credit were 200/200 and GL showed two posted movements. English
LTR and Arabic RTL messages were checked for invalid dates; duplicate, overlap and inclusive
boundary failures were visible in Arabic. The post-close rejection was exercised at the
API boundary, and missing Create permission was checked at the authenticated API boundary.
The [browser result](golden-results.json) and [screenshots](period-setup-ar.png) are retained
with an [English screenshot](period-setup-en.png).

## Audit, outbox and deviations

The golden database contained exactly one `FiscalPeriodCreateRequested`, one
`FiscalPeriodCreated`, one `finance.fiscal-period-created.v1` outbox event and one matching
idempotency receipt. Two Posted journals were balanced; the original was unchanged; period
RLS was forced. Existing regression fixtures still use direct SQL period setup to reproduce
their historical datasets. The new golden flow and local demo create periods through the
business command. The restored Close migration operation source is behaviorally verified
but is not byte-identical to the prior source manifest; its generated Designer is identical.
No unresolved acceptance blocker remains.
