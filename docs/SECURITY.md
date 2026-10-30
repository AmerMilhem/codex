# Security boundary

Every protected route authenticates before resolving current ERP membership/action. Route GUIDs select tenant/company but
do not grant access. VerifiedContext has no public constructor; callers use ContextResolver after trusted authentication.
Development adapter is opt-in, loopback-only, random bearer token, no request-supplied subject or grants; forbidden in production.
Without a real production adapter, protected routes remain denied. This is not managed OIDC/MFA readiness.

Each UoW uses one checked nonowner connection and explicit transaction, then `set_config(..., true)` for tenant/company and
correlation/request/trace. RLS fails closed for missing/invalid scope, and WITH CHECK rejects foreign writes. No session-level
tenant setting or global mutable tenant accessor is used. Worker re-resolves approved server scope every poll.
PostgreSQL GUCs are **not authentication**: arbitrary SQL execution or process compromise could change them. Parameterized
owner adapters, authorization and architecture review remain required. Schemas do not sandbox malicious code within one process.

Runtime cannot update/delete audit entries; owner mutations also hit an immutable trigger. Audit rows contain trusted actor,
tenant/company/action/entity/time/correlation/request/source; snapshot values are masked by default. Audit is transactional and
separate from application logs. Logs contain bounded identifiers/error codes, not bodies, SQL details, tokens or connection strings.
Metrics labels use bounded outcomes, not tenant IDs or request/trace IDs. Trace IDs are diagnostics, never authority.

HTTP body64KiB/maxdepth32, strict unmapped member rejection, safe Problem Details, no-store, nosniff/frame denial/referrer policy,
no permissive CORS or forwarded-header trust. Web test server sends CSP allowing same-origin scripts/styles only.
Production browser TLS/BFF/CSRF/session policy awaits real identity design; current development bearer adapter is not cookie auth.

No external network calls inside local transactions. Idempotency must reauthorize before replay, use original precondition in
fingerprint and permanent module source/effect uniqueness beyond platform key retention. No automatic expiry/cleanup has been
implemented; processing rows cannot commit incomplete. Unknown COMMIT outcome returns safe unavailable and same-intent recovery.
Outbox delivery is at-least-once; lease fencing prevents stale ACK, not duplicate sends. Idempotent consumer/external provider key
or outcome reconciliation is required. Failed/poison events remain visible; no blind success or silent drop.

No legal/compliance/JoFotara/retention/residency/PITR claims were promoted. All production credentials and cloud controls need
separate choices and verification. Dependency scans identify current published findings; they are not a security certification.
# Manual journal slice

Each Finance route independently resolves Finance.Journal.View/Create/Edit/Post against current server membership and
tenant/company. Validate requires Edit; create does not imply post. Application handlers also reject a VerifiedContext carrying
the wrong action. Foreign IDs return404 inside an allowed scope; an unauthorized route scope returns403 before business access.
Matching idempotency replays still require current Post permission. RLS and scoped FKs defend all six Finance tables.

Runtime has scoped book SELECT and guarded INSERT, scoped account/period SELECT and INSERT, and narrow period
close-metadata UPDATE grants. Reference-lock functions use fixed search_path and explicit
scope, and their non-superuser owner remains under FORCE RLS; tests prove foreign/missing scope cannot escape the functions.
The later Fiscal Period Close slice adds a scoped list, pre-close read and guarded close command with independent
`Finance.Period.View`/`Finance.Period.Close` permissions. The runtime receives UPDATE access only to the required period-close
columns; `FORCE ROW LEVEL SECURITY` remains active for runtime and migration owner after the migration commits. A deferred
constraint trigger rejects status-only close attempts without matching audit and outbox effects.
The Setup migration adds an exclusive scoped book-lock function and a deferred constraint
trigger that rejects runtime period inserts lacking creator metadata, creation audit or
the versioned outbox event. `Finance.Period.Create` remains independent from View/Close.
The Account Setup migration grants scoped account INSERT only. A guarded insert requires
canonical code, Active state and creator metadata; a deferred guard requires matching
creation audit and outbox effects. The company code unique index and scoped book lock
protect concurrent creation. `Finance.Account.Create` is independent from Account View,
Journal Post and Period permissions; FORCE RLS remains active on accounts.

Accounting Book Setup adds independent `Finance.Book.View` and `Finance.Book.Create`.
Only the verified tenant/company context can access the company's book. The runtime role
has scoped book INSERT and SELECT but no book UPDATE/DELETE. A guarded insert and deferred
effect check require Active/canonical data, creator, two audit actions and one outbox
event. The composite primary key protects the one-book-per-company constraint under
concurrent requests. FORCE RLS and connection-pool scope-reset regressions remain in the
full verification gate.
Posted header/line mutation is blocked in Domain and DB. Audit snapshots keep the existing
mask-all policy. Idempotency, journal identity and outbox are local to one atomic transaction.

The local Web token is entered in a password field, sent only on same-origin API requests, retained in memory and verified
against server-returned scope/permissions. No production login claim, token persistence or client authority is introduced.
Angular critical CSS inlining is disabled to work under strict CSP without unsafe-inline; browser checks assert actual styling.

Manual Journal Correction requires independent `Finance.Journal.Correct`; Post and Reverse never imply it. The correction relation has tenant/company FORCE RLS and scoped foreign keys. Its runtime grant is SELECT/INSERT only. The command rechecks the original, book, accounts and target period inside the transaction under locks.
