# Vertical Slice 1 — Manual Journal Entry — Acceptance

**Gate: SLICE PASS** — 2026-10-18 UTC. Scope is the authorized source/application slice and its local verification.
No production deployment, identity provider or pending legal/compliance policy is approved by this result.

## Implementation

Finance Domain/Contracts/Application/Infrastructure now implement a JournalEntry aggregate with JournalLines and Draft/Posted
states; minimum company Chart of Accounts and accounting book/fiscal-period models; create/edit/read/list/validate/post;
independent permissions; business number; immutable posted facts; scoped audit and reconciliation; durable versioned outbox
and an Inbox-deduplicated delivery trace. Angular has practical draft/list/posted flows and Arabic/English RTL/LTR support.
All other business modules remain skeletons. No AR/AP, Sales, Inventory, Manufacturing, tax, intercompany, recurring,
consolidation, allocations, workflow or reversal feature was added.

## Tests

The final complete `bash scripts/verify.sh` run exited0. Locked restore, dependency checks, Release build (0 warnings/errors),
format/type checks, tests, real browser scenario, vulnerability scans and local API/Worker publish all passed.
After visual review, clearing the stale draft-saved notice on posting was verified by rerunning the complete affected Web
gate and real golden scenario; these also exited0. Backend behavior was unchanged by that final UI-only correction.

| Suite | Passed | New |
|---|---:|---:|
| .NET Unit | 19 | 13 |
| Architecture | 9 | 0 |
| PostgreSQL/API Integration | 55 | 32 |
| Angular Unit | 11 | 6 |
| **Automated tests** | **94** | **51** |
| Shell browser acceptance checks | 9 | 1 |
| Finance golden browser acceptance checks | 19 | 19 |
| **Browser checks** | **28** | **20** |

Zero failures or skipped tests. Total tests plus named acceptance checks122, new71. Browser checks are explicitly separate
from isolated test cases; the nine SQL evidence values below are not counted again. All original Foundation regressions pass.
No currently reported NuGet/npm vulnerabilities were found. The local documented setup and API/Worker startup also passed.

## Financial Integrity

Balanced journal posts; unbalanced, invalid-side/zero lines, missing date/period, closed period, inactive/non-postable accounts
and invalid currency precision are rejected. Post revalidates after Validate. Current period/account shared locks prevent
closing/disable from interleaving before commit. Posted root/line edits, deletion and new posted lines are blocked in Domain
and through direct runtime SQL. Decimal strings/.NET decimal/NUMERIC(24,6) round trip exactly, including JOD3 at the maximum
tested amount and positive/negative internal ToEven midpoint cases. No silent rounding/balancing or correction occurs.
Per-journal and scoped global reconciliation report failures without repair.

## Security

Tenant/company membership, RLS WITH CHECK/FORCE and composite scope FKs pass negative tests. Foreign resource IDs are hidden;
unauthorized scopes are denied. Create/Edit/View do not grant Post. Revoked Post permission blocks even a matching receipt
replay. Missing context returns no finance rows; owner lock functions cannot expose foreign rows. Runtime cannot update
reference configuration. Production OIDC/MFA remains a separate existing gate; browser acceptance uses the explicit verified
Development adapter and memory-only session.

## Reliability

Concurrent matching-key contenders replay one receipt; distinct-key post contenders have one winner. Concurrent draft edits
have one winner and a412 conflict. Edit/post races cannot commit a different revision. Company/year number allocation is
transactional and unique. An injected failure after Finance and Platform local flushes rolls back journal posting, number,
audit, outbox and receipt. Same key/different original precondition returns409. A real browser request loses its response
after the database commits; retry retains original key/revision and produces no second effect. Outbox recovery/leases/Inbox
regressions from Foundation remain successful. Worker delivery continues correlation and records one deduplicated trace.

## Golden Scenario — independent SQL evidence

Company A → open2026 period → active Expense/Cash → draft → edit → Expense debit100/Cash credit100 → Validate → Post → lost
response → same-key retry → immutable posted UI → audit/worker delivery → list/reconciliation.

| Database evidence | Result |
|---|---|
| Posted journal | 1 |
| JournalPosted audit | 1 |
| Outbox event | 1 |
| Completed idempotency receipt | 1 |
| Inbox receipt | 1 |
| JournalPostedEventDelivered trace | 1 |
| Matching post → outbox → worker correlation chain with trace parent | 1 |
| Global unbalanced journals | 0 |
| Exact debit / credit | 100.000000 / 100.000000 |

## Web

Build and strict CSP pass with external CSS. Create/edit/save/validate/post/read work against the real API/PostgreSQL;
posted labels/number/actor/time and read-only behavior are checked. Exact BigInt client totals avoid Number arithmetic.
The uncertain-post state preserves its key and disables editing. Arabic/English, RTL/LTR and mobile-width containment pass.
The visual review fixed critical-CSS/CSP incompatibility, responsive grid sizing and posted-account labels; regression checks
now cover these. Screenshots below are from the final actual browser run.

## Database and Architecture

Finance migration `20261018141639_ManualJournalV1` has a separate history and frozen snapshot/security text. Clean installs,
upgrade from the previous Foundation database with retained data, reapply and no-pending-model checks pass. Scoped PK/FK,
unique account codes/posted numbers, precision/check constraints, revision, indexes and six FORCE RLS tables are enforced.
Deferred commit integrity requires financial invariants plus audit/outbox. Finance Down intentionally refuses destructive
history removal. API/Worker fail startup when required schema/RLS is missing; they never auto-migrate.

Nine architecture tests pass: Domain/Contracts/Application stay independent of persistence/HTTP, project graph remains
acyclic, other business modules cannot write Finance, and hosts/Web have no direct DB access. Shared ScopeAction metadata
was moved into delivery plumbing to avoid a module→host dependency. Platform audit reads use an owner port.

## Deviations and boundaries

Implementation refinements are recorded in [Manual Journal ADRs](../../adr/MANUAL-JOURNAL-V1.md).
One functional-currency book per company is the limited slice; FX/multiple-book functionality is deferred. Posted journal
and lines are the ledger facts rather than a duplicated financial ledger. Full CoA/period administration and closing workflow
are not invented; development configuration is explicit/additive. Draft deletion/cancel and reversals have no endpoints.
The event's initial consumer records delivery evidence only. No technology/dependency version was changed.

## CI and Blockers

The checked-in workflow enforces all critical Foundation and Finance tests, migrations, RLS, architecture, idempotency,
rollback/outbox, Web compatibility/localization, real golden browser workflow and vulnerability gates through one script.
Its final local equivalent passed. No remote GitHub Actions run or deployment is claimed; no push was performed.
There is no unresolved blocker within this slice. Existing production IdP/MFA, production operations/deployment and
Jordan/JoFotara/retention/residency verification gates remain pending. See [BLOCKERS](../../BLOCKERS.md).

## Evidence

- [Machine-readable summary](summary.json) and [source SHA256 manifest](source-manifest.json).
- [Golden browser + SQL results](golden-results.json), [Finance browser checks](journal-browser-results.json),
  [shell browser checks](foundation-browser-results.json) and [Angular unit results](angular-unit-results.json).
- [.NET Integration](Erp.IntegrationTests.trx), [Unit](Erp.UnitTests.trx), [Architecture](Erp.ArchitectureTests.trx).
- [Draft Arabic RTL](journal-draft-rtl.png), [posted Arabic RTL mobile](journal-posted-rtl.png),
  [local startup](startup-results.json), [NuGet audit](nuget-audit.json), [npm audit](npm-audit.json).
- [Full final run log](verification.txt). Raw test-owned host logs remain in ignored `artifacts/journal/`.
- [Final visual-review Web regression log](web-review.txt).
- [Contracts and local walkthrough](../../MANUAL-JOURNAL-V1.md); all prior Foundation evidence remains unchanged.

## Final Gate

**SLICE PASS**. Stop here. Proposed next vertical slice: **Trial Balance — read-only**, to verify account-level aggregation
from immutable posted journals without adding financial effects. It requires the user's separate approval; it has not started.
