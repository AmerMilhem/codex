# Manual Journal Correction — V1

`POST /api/v1/tenants/{tenantId}/companies/{companyId}/finance/journals/{id}/correct`
requires `Finance.Journal.Correct`, `If-Match` and `Idempotency-Key`. The trusted context
supplies tenant, company and actor. The body supplies an open-period posting date,
reason and complete replacement lines with decimal amounts encoded as strings. An
independent journal read returns the same `correction` relationship for the original,
reversal and replacement; the original remains Posted and immutable.

The command locks the company book and original journal, rejects Draft, prior correction,
reversal or reversal-target chains, and validates both new journals against the current
book, accounts and open period. It copies the original stored debit/credit amounts to
opposite reversal lines without recalculation. It posts the full reversal and complete
replacement with independent technical IDs and business numbers inside one transaction.
The same transaction inserts the reversal and correction relationships, posting audit,
correction audit, compact versioned outbox events and idempotency receipt. A failure at
any stage rolls all of them back. The correction's posting date applies to both new
journals; the original may belong to a Closed period.

`finance.journal_corrections` has scoped journal foreign keys and unique original,
reversal and replacement keys, a no-cycle insert guard and FORCE RLS. A deferred
constraint trigger requires all three journals Posted in one company/currency, the
matching reversal link, two correction audit actions and one
`finance.journal-corrected.v1` event at commit. No financial line or original header is
updated. One book per company remains the explicit V1 limit, enforced through existing
company/book foreign keys. Trial Balance and General Ledger consume all three ordinary
Posted journals by posting date, with no correction-specific arithmetic.

The Angular form displays the original read-only, prefills a separate editable copy,
asks for correction date and reason, and requires confirmation of the two new posted
journals. Lost-response retry retains the original key and payload. The posted-journal
screen links all three directions. Arabic/English and RTL/LTR are supported.

Run `source scripts/env.sh`, set `POC_CHROMIUM=/usr/bin/chromium`, then run
`bash scripts/verify.sh`. See [acceptance evidence](evidence/correction-v1/ACCEPTANCE.md).
