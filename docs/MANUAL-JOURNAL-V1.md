# Manual Journal Entry — Vertical Slice 1

Finance only: Draft → Validate → Post → immutable read/audit/reconciliation. No AR/AP, payments, tax, inventory, manufacturing,
intercompany, consolidation, approval workflow or reversal feature is implemented. Draft delete/cancel is omitted because no
deletion policy was approved. Existing production identity/legal/deployment gates remain as listed in BLOCKERS.md.

## Model and financial policy

JournalEntry owns ordered JournalLines and a positive monotonic revision. Draft may be empty, incomplete or unbalanced;
draft inputs still require bounded strings, valid decimal shape and existing company-scoped account IDs. Create starts at
revision1. Edit replaces the submitted line set, retains supplied owned line IDs, adds new server-assigned IDs and removes
omitted lines; every successful edit increments the root revision. Unknown/duplicate line IDs fail. No silent merge occurs.

JournalStatus is exactly Draft/Posted. Validate checks without changing status/revision and writes JournalValidated audit.
Post checks the same domain rules again under transaction locks. Posted state/lines/date are immutable at Domain/API/DB.
Posted journal and lines are the accounting ledger facts; a second mutable ledger table is unnecessary for this manual slice.

There is one minimal accounting book policy per tenant/company: functional currency plus minor units. This slice accepts
functional-currency journals only; foreign currency/rate snapshots and multiple books are deferred. Money uses .NET decimal,
PostgreSQL NUMERIC(24,6) and invariant decimal strings over HTTP. Each side is nonnegative, below10^18, at most6 fractional
places; each issued line must align to configured currency minor units. Input outside these limits is rejected before the
provider can round it. Internal `internal-to-even-v1` is a named versioned policy; positive/negative midpoint tests exist.
No automatic rounding, balancing line or residual is introduced: entered journal amounts must already be exact. JOD3 in the
development fixture proves 3-place support, not Jordan tax/legal rounding approval. Sums must also fit the amount bound.

Post requires at least2 (at most200) lines; exactly one positive debit/credit side per line; exact debit=credit; one matching
open fiscal period; active postable scoped accounts; date; matching book currency/precision. Period/account setup are minimal
models, not full administration/closing features. Account code is unique within tenant/company. Period status is Open/Closed.
Ambiguous/missing periods fail. Closed period produces FIN_PERIOD_CLOSED. Closing/disable updates wait on posting's shared
reference locks; subsequent posting rechecks current status. Approved historical period dates/year and book policy are guarded.

## Business transaction and concurrency

Post accepts strong If-Match and Idempotency-Key. Authorization is evaluated before any replay. The same authenticated
principal + tenant/company + command + target + key owns the receipt; fingerprint covers HTTP method/path/original If-Match
and canonical empty command body. Same key with changed precondition conflicts. Matching committed retries return the original
reference/revision before checking current posted state. A different key cannot post an already-posted root.

One Platform UoW/connection/transaction: idempotency claim → shared book lock → root FOR UPDATE/reload → revision/state →
shared period/account locks (accounts sorted by ID) → recheck invariants → transactional company/fiscal-year counter UPSERT →
mark Posted → flush Finance → append audit/outbox and completed receipt → final flush → deferred integrity checks → commit.
The local-only delegate remains safe for Foundation's bounded deadlock/serialization retries. No external call/event delivery
occurs inside it. Platform/Finance contexts are explicitly enlisted; SaveChanges is not an independent commit.

Technical UUIDs are separate from `MJ-{fiscalYear}-{sequence:D6}`. A counter row is scoped by tenant/company/fiscal year;
allocation rolls back with failed posting. Scoped unique constraints provide permanent posted identity independently of keys.
Concurrent draft edits return412 to a loser. Concurrent posts give exactly one financial state transition; matching-key
contenders replay it, distinct keys fail with FIN_JOURNAL_ALREADY_POSTED. The UI holds the same original key/revision after an
uncertain error and disables editing until retry resolves it. A fresh read after page reload reveals committed state.

## HTTP contracts

Prefix: `/api/v1/tenants/{tenantId}/companies/{companyId}/finance`.
All routes require verified tenant/company membership and the indicated independent permission.

| Method / route | Permission | Preconditions / result |
|---|---|---|
| GET journal-setup | Finance.Journal.View | currency/minor units + up to1000 accounts for minimal selection |
| POST journals/drafts | Finance.Journal.Create | SaveDraftRequest → reference/appliedRevision + ETag |
| PUT journals/{id}/draft | Finance.Journal.Edit | If-Match strong revision; save whole draft line set |
| POST journals/{id}/validate | Finance.Journal.Edit | If-Match; valid + stable field/line issues; no posting |
| POST journals/{id}/post | Finance.Journal.Post | If-Match + Idempotency-Key + `{}` → original command receipt + ETag |
| GET journals/{id} | Finance.Journal.View | detail + ETag; foreign resource IDs yield404 |
| GET journals?offset=0&limit=50 | Finance.Journal.View | offset0..100000; limit1..100; deterministic bounded list + hasMore |
| GET journals/{id}/audit | Finance.Journal.View | scoped immutable metadata; bounded500 records |
| GET journal-reconciliation | Finance.Journal.View | one SQL snapshot per scope; per-journal failures + overall totals, no repairs |

SaveDraftRequest contains postingDate (YYYY-MM-DD or null), currency, reference(max128), description(max1000), lines(max200).
Each line has id(null for new), accountId, debit/credit(decimal strings) and description(max256). JSON money numbers and unknown
properties are rejected. Finance-specific stable IDs use existing Foundation ProblemDetails with code/errorId/requestId/
correlationId/traceId. Example FIN_JOURNAL_UNBALANCED → finance.journal_unbalanced(422); FIN_PERIOD_CLOSED →
finance.period_closed(422); FIN_JOURNAL_ALREADY_POSTED → finance.journal_already_posted(409). Foundation concurrency412,
idempotency409 and scope403 stay unchanged. Missing/malformed If-Match uses existing request.invalid_shape400.

Audit records JournalCreated/Updated/Validated/Posted with actor issuer/subject, tenant/company, request, correlation and time.
Snapshots remain mask-all; raw financial descriptions are not copied into general audit/logs. Posted metadata is persisted on
the journal. `finance.journal-entry-posted.v1` payload contains only journalId, journalNumber, postingDate and currency; envelope
and outbox columns carry scope, correlation and trace. The Worker consumer records JournalPostedEventDelivered through the
Foundation Inbox; duplicate delivery creates neither a second audit trace nor any new financial effect. Future external
consumers/integrations need separate contracts/approval. This is local durable at-least-once delivery, not an exactly-once claim.

## Local UI and verification

Run README steps, including `python3 scripts/local.py finance-demo`. Open Web `/workspace`; use tenant
`aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa`, company `11111111-1111-4111-8111-111111111111` and the token from the ignored local
settings file (view it privately; do not put it in URLs/history/logs). The token is verified server-side and stays only in memory.
Open Manual journals → New journal → date → Expense debit100 / Cash credit100 → save → validate → post. View the number,
actor/time, totals/lines/audit; return to list and reconcile. Posted fields are disabled and edit/post actions disappear.
The form's BigInt scaled arithmetic preserves totals without binary floating-point. Arabic/English toggle sets RTL/LTR and
does not change wire date or decimal semantics. No client-side permission or validation bypasses the API.

`bash scripts/verify.sh` runs the full source gate. Focused backend: `dotnet test tests/Erp.IntegrationTests --filter
FullyQualifiedName~ManualJournal`. Focused real browser after Release build + Angular build:
`python3 scripts/test-journal-e2e.py`. The runner owns and removes only its isolated test container/processes, preserving reports.
CI executes the same gate; remote execution is not claimed until a user-authorized push. See the retained slice evidence for
counts, golden SQL checks and limitations. No next slice starts automatically.
