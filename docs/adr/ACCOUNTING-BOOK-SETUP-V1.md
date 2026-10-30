# ADR: Make the existing company book explicit

**Status:** Implemented for Vertical Slice #8.

The preexisting `finance.accounting_books` policy row is the book. We retain its
tenant/company composite primary key and the existing dependent foreign keys, enforcing
one book per company rather than introducing unsupported multi-book semantics. An
additional unique technical ID provides a stable external identity. No child book IDs
or account scope changes are necessary while this constraint holds. Any later multi-book
design needs a separate migration and posting/reporting contract.

Historical IDs, currency and precision remain unchanged. The new legacy ID is a stable
hash of tenant/company IDs; the `PRIMARY` code and legacy name are labels, with creator
metadata left null. New books use an explicit currency/precision allowlist and Active
state. A scoped advisory lock, composite primary key, RLS and guarded transaction protect
duplicates and command effects. Migration generation uses
`scripts/add-finance-migration.sh` after a fresh build; no automatic downgrade is allowed
for the identity backfill.
