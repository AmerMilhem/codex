# Chart of Accounts Setup — acceptance record

**Gate: SLICE PASS.** The final `scripts/verify.sh` run completed on 2026-10-24.
Its output is [verification.txt](verification.txt); machine-readable counts and hashes
are in [summary.json](summary.json).

## Full regression

The final run passed 63 .NET unit, 9 architecture, 128 PostgreSQL integration and 50
Angular tests: **250 automated tests**, all passed, none skipped or failed. This adds
24 tests to the approved Fiscal Period Setup baseline. Eight browser runners passed
**155 named checks**, including 19 new account-setup checks. Release build and format,
dependency boundaries, NuGet/npm vulnerability gates, and local API/Worker publish passed.
These are local CI-equivalent results; remote CI and production deployment were not run.
The [integration TRX](Erp.IntegrationTests.trx), [unit TRX](Erp.UnitTests.trx),
[architecture TRX](Erp.ArchitectureTests.trx) and [Angular result](angular-unit-results.json)
retain the exact test results.

## Account integrity and security

The application normalizes codes to trimmed uppercase ASCII and rejects empty/invalid
codes and names. Its scoped book-row lock, normalized duplicate check, database insert
trigger and existing company code unique index prevented concurrent duplicates; a legacy
lowercase code was also protected. The same code was accepted in another company. New
accounts began Active, persisted PostingAllowed, creator and creation time. Journal Post
accepted a new postable account and rejected a new non-postable header with the stable
`FIN_ACCOUNT_NOT_POSTABLE` error. Tests covered independent Account Create/View permissions,
unauthorized HTTP requests, tenant/company isolation, pooled scoped reads and forced RLS.

Sequential and concurrent identical idempotency retries returned one account. Retrying
after a committed but lost response reused its identity and created no second effect;
the same key with changed code or name conflicted. Injected failure after persistence
rolled back account, audit, outbox and receipt. A direct runtime INSERT without the
required command effects failed at commit. Worker Inbox duplicate delivery wrote one
operational trace and did not mutate the account.

## Migration and existing data

The [migration chain check](migration-chain-check.json) records the exact five Finance
migrations, with Chart of Accounts Setup after Fiscal Period Setup. The previous Setup
Designer matches the approved source manifest. The PostgreSQL suite migrated a clean
database to latest with no pending model changes and verified creator columns, constraints,
scoped lock function and FORCE RLS. A second database advanced sequentially from prior
schemas through Close, Period Setup and Account Setup. It retained Open/Closed period
metadata, two prior account IDs/codes/status/postability values and a balanced Posted
journal with its period relationship. A pre/post digest of historical financial fields
matched. Legacy creator fields remained null, with no invented actor. The earlier
[Fiscal Period Setup incident](../period-setup-v1/MIGRATION-INCIDENT.md) remains recorded;
the new migration used the fresh-build wrapper.

## Browser golden scenario

The disposable database began with a company accounting book and **no SQL-seeded accounts
or fiscal periods**. The browser created account 6100 Office Expense, simulated response
loss after its actual commit and retried with the same key and payload; it then created
1100 Cash and non-postable 1000 Assets Header. It created an Open period through the existing
UI, validated and posted a 100/100 Manual Journal on the two new postable accounts, read
the Posted journal, reconciled Trial Balance and read GL movements. The server rejected
a Post using the header account. Browser/UI/API checks also covered invalid and duplicate
codes, independent Create permission, Arabic/English error messages and RTL/LTR. The
[golden result](golden-results.json) retains 19 named checks and [Arabic](account-setup-ar.png)
and [English](account-setup-en.png) screenshots.

The golden database contained exactly three accounts, three AccountCreateRequested audit
rows, three AccountCreated audit rows, three `finance.account-created.v1` outbox events
and three completed account-creation receipts. It contained one balanced Posted journal
with two lines on the new accounts; account RLS was forced.

## Declared deviations and limits

The approved model has one Unicode account `Name` field, so it accepts Arabic or English
but does not maintain separate translations. Existing regression fixtures still directly
insert reference accounts to reproduce earlier datasets; the new golden flow and local
demo do not. The first 1000 company accounts are listed without a new paging framework.
The user-facing golden flow uses UI for account/period creation, Journal Post and reports;
the non-postable rejection and missing-permission case are also asserted at the API
boundary. No unresolved acceptance blocker remains.
