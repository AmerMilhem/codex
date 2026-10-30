# Migration ownership and operation

Platform.Infrastructure owns EF model/migrations for `integration` and `audit`.
`public.__EFMigrationsHistory` is controlled global version metadata, with read-only runtime grant.

- `20261018000100_InitialFoundation`: four tables, scoped RLS WITH CHECK/FORCE, runtime grants, immutable audit guard,
  and deferred validation rejecting committed Processing idempotency receipts.
- `20261018000200_IndexOutboxLeaseRecovery`: additive filtered index; preserves N rows while N+1 is applied.

Each migration has a generated frozen EF model snapshot/metadata. SecuritySql.Version1 is frozen migration text, not a mutable
runtime policy generator. Future scope/security changes get another migration. Do not modify an applied migration.

The runner `Erp.Migrations` consumes only `ERP_MIGRATION_CONNECTION`, verifies separate non-superuser identity and applies
latest migrations. API/Worker do not apply migrations on startup; they fail readiness/startup if current schema/role protection is absent.
The development runner creates `erp_migrator` as DB owner and `erp_runtime` as a separate limited login before applying migrations.

```bash
dotnet tool restore
dotnet ef migrations add DescriptiveName --project src/Platform/Erp.Platform.Infrastructure --output-dir Persistence/Migrations
# Review generated DDL, scope constraints, privileges and compatibility before applying.
dotnet run --project src/Hosts/Erp.Migrations --no-build
```

Migration integration tests create a clean DB, apply N only, seed a pending outbox row, upgrade to current, repeat the migration
operation, verify the row survives, and assert no pending model changes. This proves local N→N+1 behavior, not cloud/fleet rolling deployment.
For future changes use expand/backfill/contract and retire incompatible writers before contract. Initial Down removes foundation
tables and is destructive; never treat it as routine rollback or run it against retained data without a separately reviewed plan.

Finance.Infrastructure independently owns `20261018141639_ManualJournalV1`, its generated snapshot and
`public.__FinanceMigrationsHistory`. The controlled runner applies Platform then Finance, keeping each context's history distinct.
Finance adds accounting_books/accounts/fiscal_periods/journal_entries/journal_lines/journal_numbers; composite scope FKs,
account-code/posted-number uniqueness, decimal(24,6), optimistic revision, scoped indexes and FORCE RLS on all six tables.
Runtime has SELECT-only access to book/account/period configuration. Three narrow SECURITY DEFINER lock functions have fixed
`search_path=pg_catalog`, explicit transaction scope predicates, no dynamic SQL and an owner still subject to FORCE RLS. They
acquire shared row locks without granting runtime UPDATE on reference tables; PUBLIC EXECUTE is revoked. Runtime is never
the table/function owner. Tests verify missing/foreign scope and denied reference writes through the actual runtime login.

Posted header/line guards prohibit mutation/deletion/new lines; a deferred commit guard checks the final financial state and
requires JournalPosted audit plus exactly one posting outbox row. Currency policy and period dates/year cannot reinterpret
existing history. Finance Down throws intentionally: retained financial history requires a reviewed forward recovery.
New integration tests install cleanly, upgrade a previous Foundation database with retained data, reapply, compare snapshots
and check runtime readiness. Do not edit applied migrations; security SQL V1 is frozen alongside its migration.

Trial Balance adds no schema migration or index. Actual broad-history and selective-date plans evaluate the existing scoped
journal status/date, journal-line and account indexes. Ledger history remains the only source; no balance field or cache is added.

General Ledger Detail V1 adds no migration or index. It reads existing posted journals/lines and existing scoped account indexes; measured query plans are retained in [GL evidence](evidence/general-ledger-v1/ACCEPTANCE.md). Existing clean-install/upgrade and immutable-ledger regression checks remain mandatory.

Manual Journal Reversal V1 adds Finance migration `20261020202816_ManualJournalReversalV1`. It creates only
`finance.journal_reversals`: a scoped primary key on the original, a scoped unique key on the reversal, two scoped journal FKs,
required reason/actor/request/correlation metadata, and FORCE RLS. Runtime receives SELECT/INSERT only. A trigger rejects
UPDATE/DELETE and reversal chains while locking both roots; a deferred constraint trigger requires two Posted journals in the same
tenant/company/currency, exact position-by-position debit/credit inversion, the two reversal audit actions, and exactly one
`finance.journal-entry-reversed.v1` outbox record before commit. Clean installation and upgrade from the previous Finance migration
are integration-tested with retained journal history.

Fiscal Period Close V1 adds Finance migration `20261023152528_FiscalPeriodCloseV1`: revision, close actor/time/comment and
reconciliation evidence on `finance.fiscal_periods`, with a consistency check and guarded Open→Closed transition. No generic
configuration or year-end tables are created. Because the migration owner is subject to FORCE RLS, the migration transaction
temporarily removes FORCE for an all-tenant legacy Closed-row backfill and restores it before commit. Legacy values explicitly
record unverified evidence. Clean installation and upgrade from the previous Finance schema are tested. Runtime receives only
the column-level UPDATE grant needed by the command; tenant/company RLS remains forced afterward.

Fiscal Period Setup V1 follows Close with `20261023212645_FiscalPeriodSetupV1`. It adds
nullable creation metadata only, preserving all old Open/Closed rows and financial links;
new runtime inserts require creator metadata plus two audit rows and one outbox event.
The exclusive book-lock function and guarded insert use the existing FORCE RLS scope.
The migration chain and prior data preservation are tested in
`tests/Erp.IntegrationTests/FiscalPeriodMigrationTests.cs`. Use
`scripts/add-finance-migration.sh MigrationName` to build immediately before EF generation.
The [generation incident](evidence/period-setup-v1/MIGRATION-INCIDENT.md) explains why
stale `--no-build` metadata must not be used for migration removal.

Chart of Accounts Setup V1 follows Fiscal Period Setup with
`20261024162900_ChartOfAccountsSetupV1`. It adds nullable account creator metadata,
preserving legacy account and journal values. Runtime account inserts require canonical
codes, Active state, creator metadata, two audit actions and one outbox event at commit.
The scoped book-row lock and existing company code unique index protect concurrent
creation. The clean and sequential upgrade tests cover all five Finance migrations,
legacy accounts, periods and posted journal history. Generate future Finance migrations
only through `scripts/add-finance-migration.sh`.

Accounting Book Setup V1 follows Account Setup with
`20261024211908_AccountingBookSetupV1`. It adds ID, code, name, Active status and
nullable creator metadata to the existing company book row; it does not replace its
composite key or dependent financial foreign keys. The migration assigns deterministic
IDs and legacy labels to existing rows while preserving currency/precision and all
historical periods, accounts, journals, reversal and closing data. FORCE RLS is removed
only within the migration transaction for the all-company backfill and restored before
commit. Clean and sequential upgrade tests now cover all six Finance migrations and
assert historical row digests and no pending model changes. Do not run EF tooling on a
stale build: use `scripts/add-finance-migration.sh` after a fresh build.

Manual Journal Correction V1 follows Accounting Book Setup with `20261029112730_ManualJournalCorrectionV1`. It creates only a scoped immutable three-journal relationship with unique keys, journal foreign keys, FORCE RLS, guard and deferred integrity trigger. Clean and sequential upgrade tests cover all seven Finance migrations and historical rows. Generate future migrations with `scripts/add-finance-migration.sh`.
