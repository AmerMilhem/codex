# Tests and CI

`scripts/verify.sh` is the common local/CI entry point. It retains underlying exit statuses and fails on setup, format,
dependency or test failures. Do not run builds/formatters concurrently on the same checkout; MSBuild parallelism is bounded.

| Layer | Real verification |
|---|---|
| Unit | canonical fingerprints/duplicate JSON, membership changes/company scope, masking and error/revision rules |
| Architecture | project/compiled references, Domain/Contracts package guards, cross-module negatives, cycles, Web/host DB bans |
| PostgreSQL integration | actual pinned18.x Testcontainer, nonowner runtime, two DbContexts, audit/outbox/idempotency atomicity, RLS/pool/cancel/rollback |
| Concurrency | simultaneous idempotency contenders, permanent source uniqueness, bounded SQLSTATE retry, stale EF revision |
| Recovery | outbox leases, fenced ACK, backoff/Failed, exhausted lease, Inbox collision/atomic rollback, physically killed worker processes |
| HTTP/telemetry | trusted adapter, current membership, strict JSON, error codes, correlation/trace through HTTP→DB→outbox→worker |
| Migrations | clean install, repeatability, N→N+1 with existing data and snapshot model check |
| Angular | approved tuple build, TestBed/Vitest services/directive/API tests, Playwright route/language/RTL/reload/narrow viewport checks |

`foundation_test` schema exists only in integration containers. State/effect dummy tables prove local invariants; they are
not Finance or Inventory implementations and never enter runtime migrations. A deferred test constraint rejects a state
commit missing its required effect/audit/outbox. A separate CrashHarness is test-only and not published with API/Worker.

Named pool MaxPoolSize1 checks physical PID reuse A→B and missing context after commit/rollback/cancel. Worker tests verify
its company scope and required worker permission. Retry injection uses real PostgreSQL SQLSTATE errors to exercise full-UoW
retry/rollback; no external side effects are allowed inside that delegate. Unknown commit is never blindly retried.

CI `.github/workflows/foundation.yml`: SHA-pinned official actions, read-only repository permissions, pinned SDK/Node,
locked restore/npm ci, builds/format/type checks, critical tests, NuGet/npm vulnerability gates and API/Worker artifact generation.
It never deploys. A workflow file plus local execution does not establish a remote GitHub Actions run; that awaits a reviewed push.
TRX/browser/audit reports live in ignored `artifacts/`; the final retained summary is in `docs/evidence/FOUNDATION-V1.md`.

The same CI entry point now requires ManualJournal domain/API/PostgreSQL tests and Angular journal tests. It also runs
`scripts/test-journal-e2e.py`: isolated real API+Worker+Angular+PostgreSQL, Cash/Expense100, draft edit, validate, post,
deliberately lost HTTP response AFTER actual DB commit, same-key retry, immutable posted screen, Arabic/English RTL/LTR,
worker/inbox delivery and independent SQL reconciliation. Browser transport loss is injected; financial API/database responses
are not mocked. Unit UI tests separately simulate error responses to verify retry state and permission visibility.

New database cases cover concurrent edits/posts/number allocation, closed-period/account races, rollback after all local writes,
3-place precision at the numeric bound, reference lock-function scope, scoped FKs and unique account codes, posted mutation
through SQL, migration from Foundation and fresh install. Reconciliation only reports failures and never repairs data.
Artifacts: `artifacts/journal/golden-results.json`, browser-results, screenshots and process logs. The retained slice report and
machine-readable evidence live under `docs/evidence/journal-v1/`; historical Foundation evidence is preserved unchanged.

Trial Balance adds exact arithmetic/range tests, real PostgreSQL aggregation/date/closed-period/RLS/pool/reconciliation and
no-write regressions, plus Angular state/language/permission tests. Its independent browser runner posts 100/40 golden fixtures,
excludes a draft, adds later/prior-year journals, compares Arabic/English numbers and refreshes a closed period with identical
before/after digests across Finance/Audit/Integration tables. No response or financial figures are mocked in the browser.
The performance case builds 100,000 constrained ledger lines, measures equivalent SQL/EF projections and the complete report,
and retains EXPLAIN ANALYZE BUFFERS JSON for broad and selective dates. Preparation time is separate from measured reads.
The test-only bulk-insert timeout is180s; runtime report commands retain30s. No fragile millisecond pass threshold or SLA is invented.
`scripts/verify.sh` requires all previous suites plus `scripts/test-trial-balance-e2e.py`; evidence is retained separately in
`docs/evidence/trial-balance-v1/`. Corruption tests use a disposable superuser fixture to prove observable failures without repair.

General Ledger V1 adds unit, integration, Angular and real browser tests. `scripts/verify.sh` enforces `scripts/test-general-ledger-e2e.py` after Trial Balance. The 102k-line performance fixture records opening, count/period, paged window and TB reconciliation plans; see [GL guide](GENERAL-LEDGER-V1.md).

Manual Journal Reversal V1 adds domain, PostgreSQL/HTTP concurrency and failure, Angular, and real-browser tests.
`scripts/verify.sh` runs `scripts/test-reversal-e2e.py` after every prior Finance acceptance runner. The golden runner posts an
original in January, closes January, reverses it in February after deliberately losing the committed HTTP response, retries with
the same key, and independently checks one relation/effect, exact opposite lines, immutable history, audit/outbox, Period A/B and
combined Trial Balance, and General Ledger 100→0. Different-payload retries, new-key duplicates, permissions and cross-scope access
are rejected. Browser assertions use the actual API and PostgreSQL; no financial response is mocked.

Fiscal Period Close V1 adds domain, PostgreSQL/HTTP, Angular and real-browser tests. `scripts/verify.sh` runs
`scripts/test-period-close-e2e.py` after all preceding Finance runners. The golden runner posts 100 into an Open period,
previews, closes through Angular, simulates a lost HTTP response after commit, retries with the same key, then checks one
period transition, receipt, two audit actions and one outbox event directly in PostgreSQL. It checks Post and Reversal rejection
after close, unchanged Trial Balance and General Ledger, and Arabic/English RTL/LTR. Integration tests cover failed
reconciliation/rollback, concurrency, cross-scope/permission, RLS, worker Inbox trace and previous-schema upgrade.

Fiscal Period Setup V1 adds domain, PostgreSQL/HTTP, Angular and real-browser tests.
`scripts/verify.sh` runs `scripts/test-period-setup-e2e.py` after all prior runners.
The new golden database has no SQL-seeded fiscal period: the browser creates one through
Angular, retries after a lost committed response, validates/posts/reverses a journal,
reads TB/GL, closes through the existing UI, proves a later Post is blocked, and reads
reports again. Negative browser checks cover dates, exact/partial/boundary overlap,
permission and localized errors. Sequential migration tests verify the exact chain and
preserve prior period and posted-journal history.

Chart of Accounts Setup V1 adds domain, PostgreSQL/HTTP, Angular and real-browser tests.
`scripts/verify.sh` runs `scripts/test-account-setup-e2e.py` after every preceding runner.
Its disposable database starts with a company book but no period or accounts. Angular
creates the period and three accounts, retries a committed request after losing the
response, validates/posts a journal on newly created accounts, reads TB/GL, rejects a
non-postable header and checks duplicate/permission/localized errors. Migration tests
exercise clean install and sequential upgrade preserving prior accounts and posted journals.

Accounting Book Setup V1 adds domain, PostgreSQL/HTTP, Angular and real-browser tests.
`scripts/verify.sh` runs `scripts/test-book-setup-e2e.py` after preceding runners. Its
disposable database starts without a book, period or account. The browser creates each
through its Finance screen, validates/posts/reverses, reads TB/GL, closes the period and
checks that posting afterward fails. Negative checks cover duplicate, currency, permission,
response-loss retry and localization. Migration tests retain legacy JOD and EUR book
policies plus Open/Closed periods, accounts, posted original/reversal journals and close
history across a sequential upgrade.

Manual Journal Correction adds PostgreSQL/HTTP, Angular and browser acceptance. `scripts/verify.sh` runs `scripts/test-correction-e2e.py` after prior runners. The disposable golden browser creates Book, Periods and Accounts through the UI, closes the original period and posts reversal plus replacement into a later open period, then checks bidirectional navigation, reporting and lost-response retry.
