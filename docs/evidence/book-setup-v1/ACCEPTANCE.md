# Accounting Book Setup — acceptance record

**Gate: SLICE PASS.** The final `scripts/verify.sh` run completed on 2026-10-24 with
exit code 0. See [full output](verification.txt), [machine summary](summary.json) and
[source hashes](source-manifest.json). These are local CI-equivalent checks; remote CI
and production deployment were not run.

## Full regression

The final run passed 71 .NET unit, 9 architecture, 137 PostgreSQL integration and 55
Angular tests: **272 automated tests**, all passed, none skipped or failed. This adds 22
tests to the approved Chart of Accounts Setup baseline (8 .NET unit cases, 9 integration
facts and 5 Angular cases). Nine browser runners passed **177 named checks**, including
22 new Book Setup checks. Release build, formatting, dependency boundaries, NuGet/npm
vulnerability gates and local API/Worker publish passed. The [unit](Erp.UnitTests.trx),
[integration](Erp.IntegrationTests.trx), [architecture](Erp.ArchitectureTests.trx) and
[Angular](angular-unit-results.json) results retain exact test evidence.

## Book integrity, reliability and security

The model retains the existing one-book-per-company composite primary key and child
foreign keys. Codes are uppercase ASCII with trimmed outer whitespace; name and currency
validation reject invalid input. New books start Active. An application-scoped advisory
lock, existing composite primary key, unique indexes and guarded runtime insert prevent
concurrent duplicate creation. Independent Book View/Create permissions, tenant/company
scope, FORCE RLS and repeated pooled scoped reads passed. A direct runtime INSERT without
the required effects fails at commit.

Sequential and concurrent same-key retries return one book and one outbox event. A
committed-but-lost response is safely retried with the original payload/key; changed
payload with the same key conflicts. Injected post-insert failure rolls back book, audit,
outbox and receipt. The golden database has exactly one book, two creation audit actions,
one `finance.accounting-book-created.v1` outbox event and one completed receipt.

## Migration and existing data

The [exact chain](migration-chain-check.json) has six Finance migrations, with Accounting
Book Setup after Chart of Accounts Setup. Clean install reaches latest, has FORCE RLS,
the expected constraints/functions and no pending EF model changes. A separate database
advanced sequentially through the previous Finance schemas to the new migration. It
contained JOD/3 and EUR/2 legacy books, Open and Closed periods, close actor/time/comment
and reconciliation, two accounts, a Posted journal and its Posted reversal, lines and
the reversal link. A digest of historical business fields matched before and after each
step; IDs, dates, numbers, amounts, links, account state and close history were preserved.
Legacy book IDs are deterministic by tenant/company, currencies/precision unchanged,
new creator fields null, and repeat migration created no duplicate book. FORCE RLS was
restored before migration commit. Migration generation used the fresh-build wrapper.

## Browser golden scenario

The disposable database began with **no operationally seeded book, period or account**.
The [browser result](browser-results.json) and [database result](golden-results.json)
show UI creation of PRIMARY/JOD, an Open period, Cash and Expense accounts, a balanced
100/100 journal, Validate/Post, Trial Balance, GL, Reversal and Close. The Closed period
blocked a subsequent Post; reports remained readable and reconciled. The browser
simulated response loss after committed book creation and safely retried, then checked
duplicate/different-code, invalid currency and unauthorized Create responses. Arabic RTL
and English LTR/screens/errors passed; [Arabic](book-setup-ar.png) and
[English](book-setup-en.png) screenshots are retained. Database inspection found one
Active:JOD:3 book, one Closed period, two accounts, two balanced Posted journals and
one reversal link, with forced RLS.

## Declared deviations and limits

V1 deliberately supports one book per company. Existing Accounts, Periods and Journals
remain company-scoped, so TB/GL cannot mix books within a company; no multi-book selector,
parallel posting or new child `book_id` columns were introduced. The explicit currency
allowlist governs new books only and never rewrites older book currencies. Historical
regression fixtures use privileged direct SQL to reproduce old states, while the new
golden flow and local demo use controlled UI commands. No unresolved acceptance blocker
remains.
