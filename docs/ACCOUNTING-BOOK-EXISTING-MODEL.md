# Accounting book baseline before Slice #8 migration

The existing `finance.accounting_books` table has **no technical Book ID**. Its primary key
is `(tenant_id, company_id)`, allowing exactly one policy row per company. `currency` is
the functional currency and `minor_units` defines permitted monetary precision. Existing
Manual Journal posting checks its currency and line precision against this row.

`finance.fiscal_periods`, `finance.journal_entries`, `finance.accounts` and
`finance.journal_numbers` each have a foreign key to that same composite book key.
Journal lines reference company-scoped journals and accounts; reversals reference posted
journals. Accounts are **company-scoped**, not individually book-scoped. Trial Balance and
GL read the company's sole book currency and minor units while aggregating posted journal
lines within the verified tenant/company. Period overlap, journal numbering and reporting
all use that same one-book-per-company scope.

The minimal safe evolution is to add an explicit stable Book ID, code, name, status and
nullable creator metadata to this existing row. Keep the composite primary/foreign keys:
they continue to identify the one valid book for each company without rewriting any
period, account, journal, line, number, reversal or audit row. Existing book rows become
`PRIMARY`/`Legacy Accounting Book`/`Active`, receive a deterministic ID based on their
tenant and company IDs, and retain their existing `currency` and `minor_units` exactly.
The legacy label identifies migrated policy, not a historical actor. New books are created
only through the authorized command. This slice explicitly supports **one book per
company**, with no multi-book query or posting contract.
