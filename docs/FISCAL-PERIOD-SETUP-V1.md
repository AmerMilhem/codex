# Fiscal Period Setup — V1

The Finance business command `POST /api/v1/tenants/{tenant}/companies/{company}/finance/periods`
accepts inclusive `startDate` and `endDate` plus `Idempotency-Key`. Tenant and company come
from verified context. `Finance.Period.Create` is separate from View and Close. A new period
starts Open at revision 1; its technical ID, creator and creation time are set by the server.
Fiscal-year numbering uses the year in which the period starts, including a period spanning
calendar years. A missing period retains the existing posting/reversal error policy.

The application rejects missing/reversed dates, an exact duplicate and any overlap within
the same company book. Both date bounds are inclusive: Jan 1–31 conflicts with Jan 31–Feb 28,
while Feb 1–28 is adjacent. The command takes an exclusive scoped book-row lock before
checking overlap and inserting. The database trigger uses the same lock and interval check,
so concurrent requests cannot both create overlapping rows. Existing period dates/status
are never rewritten.

One transaction persists the period, `FiscalPeriodCreateRequested` and
`FiscalPeriodCreated` audit actions, the compact `finance.fiscal-period-created.v1` outbox
event, and the idempotency receipt. A deferred database guard rejects runtime inserts lacking
creator metadata or these effects; RLS remains forced. The worker validates event identity
against the persisted period and records one Inbox-deduplicated operational trace. Matching
retries return the same period ID; a reused key with different dates conflicts.

The existing Angular periods screen now includes a permission-gated creation form, bounded
existing-period list, localized business errors, and frozen dates/key for a retry after an
unknown response. The newly created period works with ordinary Journal Validate/Post,
Reversal, Trial Balance, GL and Fiscal Period Close. No reopening, editing, templates,
multi-ledger calendar or year-end flow was added. Legacy test fixtures still seed reference
periods directly to reproduce prior-slice datasets; the new browser golden scenario and
local demo no longer depend on a direct period seed.

Run `source scripts/env.sh`, set `POC_CHROMIUM=/usr/bin/chromium`, then
`bash scripts/verify.sh`. The [acceptance evidence](evidence/period-setup-v1/ACCEPTANCE.md)
includes sequential migration and retained-history results. Create future Finance
migrations only with `scripts/add-finance-migration.sh MigrationName`, which compiles the
model immediately before invoking EF tooling.
