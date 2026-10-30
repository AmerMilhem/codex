# Chart of Accounts Setup — V1

`POST /api/v1/tenants/{tenant}/companies/{company}/finance/accounts` is a Finance
business command requiring `Finance.Account.Create` and an `Idempotency-Key`. The tenant,
company and actor come from verified context. The request contains `code`, `name` and
`postingAllowed`; the server assigns ID, Active status, creator and creation time.
`GET /accounts` requires the independent `Finance.Account.View` permission and returns
the company's first 1000 accounts in code order. The Journal setup endpoint still provides
the existing account selection and checks Postability on the server at posting time.

Code normalization trims outer whitespace and uppercases invariantly. New codes must be
1–32 ASCII characters matching `[A-Z0-9][A-Z0-9._-]{0,31}`; interior whitespace and other
characters are rejected. Names are trimmed and must contain 1–200 characters; the current
single `Name` field accepts Arabic or English text, with no translation model. The
application checks duplicates against normalized legacy codes under an exclusive scoped
book-row lock. The existing company-scoped unique code index and database insert trigger
provide a second, concurrency-safe defense. The same code is allowed in another company.

One transaction inserts the account, two audit actions (`AccountCreateRequested`,
`AccountCreated`), one compact `finance.account-created.v1` outbox event and the idempotency
receipt. A deferred trigger rejects runtime inserts missing required effects. Worker Inbox
delivery records an operational trace without changing the financial account. New creator
columns are nullable for legacy accounts and immutable after creation. Existing accounts,
periods and posted journals are not rewritten.

The Angular Chart of Accounts page lists scoped accounts and exposes Create only with its
permission. A lost response retains the original code, name, PostingAllowed value and key
for retry. Arabic/English and RTL/LTR are supported. The local `finance-demo` prepares only
the development identity; create the company book, fiscal period and accounts through
their Web screens. No account edit/delete, hierarchy, dimensions, import or multi-ledger setup is
included. Run `source scripts/env.sh`, set `POC_CHROMIUM=/usr/bin/chromium`, then run
`bash scripts/verify.sh`. See [acceptance evidence](evidence/account-setup-v1/ACCEPTANCE.md).
