# Accounting Book Setup — V1

The [pre-migration model](ACCOUNTING-BOOK-EXISTING-MODEL.md) already had one implicit
accounting book per tenant/company. This slice gives that same row a stable technical ID,
canonical code, name, Active status and nullable creator metadata. Its original
`(tenant_id, company_id)` primary key and all dependent foreign keys remain intact. **V1
supports exactly one book per company.** Accounts, fiscal periods, journals, Trial Balance
and GL retain their existing company scope; there is no second ledger or book selector.

`POST /api/v1/tenants/{tenant}/companies/{company}/finance/books` is the sole runtime
creation command. It requires `Finance.Book.Create` and `Idempotency-Key`. The verified
context supplies tenant, company, actor, request and correlation; the client supplies only
code, name and functional currency. `GET /books` requires independent `Finance.Book.View`.
An existing book makes a second creation fail with `FIN_BOOK_DUPLICATE`, whatever its
code. Book status starts Active and has no update/delete command.

Codes are trimmed, uppercased and must match `[A-Z0-9][A-Z0-9._-]{0,31}`; names are
trimmed and must contain 1–200 characters. New currency/precision pairs are JOD, KWD,
BHD, OMR/3; USD, EUR, GBP, SAR, AED/2; JPY/0. Unsupported codes fail
`FIN_BOOK_CURRENCY_INVALID`. This allowlist validates new books only; the migration
**copies each legacy currency and minor-units value unchanged**, including values outside
the current new-book allowlist. Manual Journal remains functional-currency only; no FX
conversion or parallel posting was added.

The command acquires a company-scoped transaction advisory lock, checks for an existing
book, then atomically writes the book, `AccountingBookCreateRequested` and
`AccountingBookCreated` audit actions, one compact
`finance.accounting-book-created.v1` outbox event and an idempotency receipt. The
composite primary key protects concurrent different-key attempts. A guarded runtime
insert and deferred trigger require canonical data and matching effects; FORCE RLS and
company scoping remain active. Exact-key retries replay the result, while a changed
payload with the same key conflicts. The Angular page lists the single book and permits
Create only with its separate permission; a lost response preserves the original key and
payload for retry. Arabic/English and RTL/LTR are supported.

The migration creates a deterministic legacy ID from tenant/company UUIDs, assigns
`PRIMARY` and `Legacy Accounting Book`, and leaves historical `CreatedAt/CreatedBy` null.
It never re-creates or edits historical periods, accounts, journals, amounts, reversals,
close metadata or audit. The migration transaction temporarily disables FORCE RLS for
the all-company backfill, then restores it before commit. Automatic Down is blocked to
protect book identity and history. New development databases create the book, period and
accounts through their respective Web screens; `finance-demo` supplies only a local
identity. See [migration tests](../tests/Erp.IntegrationTests/FiscalPeriodMigrationTests.cs)
and [acceptance evidence](evidence/book-setup-v1/ACCEPTANCE.md).
