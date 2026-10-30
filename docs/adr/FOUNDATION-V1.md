# Foundation ADR updates

| ADR | Decision / evidence | Consequence / revisit |
|---|---|---|
| F01 | .NET modular monolith with separate Api/Worker/Migration hosts | no microservices or production deployment |
| F02 | four explicit skeleton assemblies per seven business modules | user expressly requested visible boundaries; overrides earlier create-incrementally guidance, no business features |
| F03 | shared Platform adapter owns only Audit/Integration schemas | business Infrastructure may use its UoW plumbing; still cannot reference another business module Infrastructure |
| F04 | authenticated adapter→current membership→immutable scope→transaction-local RLS | RLS complements authorization; not a defense against arbitrary SQL/process compromise |
| F05 | one connection/transaction, explicit context enlistment, bounded full-UoW 40001/40P01 retry | delegates must be local-only; stable intent/effect IDs prepared by owner; unknown commit never blind retry |
| F06 | transaction-scoped idempotency claim/completed receipt + permanent module source/effect constraint | deferred trigger rejects orphan committed Processing rows; retention cleanup deliberately absent |
| F07 | Outbox leasing/backoff/Failed + atomic Inbox/effect + trace continuation | at-least-once; no broker/exactly-once claim; unknown handlers fail visibly |
| F08 | Audit append-only with mask-all default snapshots | later allowlisted masking policy needs security review; application logs remain separate |
| F09 | uppercase error registry IDs retain approved dotted wire codes | no silent wire-contract renaming; code/errorId both stable, localized prose is not client logic |
| F10 | local bearer Development/Test adapter + default-deny Web session abstraction | real OIDC/MFA/session/cookie policy still blocked; no fake production identity |
| F11 | fresh N migration and additive N+1 recovery index, distinct migration credentials | test clean install/upgrade/reapply/data survival; no fleet/PITR claims |
| F12 | Angular zoneless native shell, lazy routes, runtime Arabic/English, logical CSS | no commercial UI/grid, no ERP screens; Foundation language choice has no accounting effect |
| F13 | OTel instrumentation without invented exporter destination; JSON logs/outcome-only metrics | exporter injected in test; managed collector/provider settings remain operational decision |
| F14 | Worker uses bounded polling; Quartz remains pinned candidate outside current dependency graph | no scheduling requirement here; avoid unneeded persistent scheduler store until a real use case |

Deviations: supporting Platform namespaces share one adapter assembly as allowed by Blueprint; business skeletons were created
up front at the latest user's explicit instruction; error IDs are aliases rather than a second wire vocabulary; test probes
live exclusively in test infrastructure; legacy PoC is isolated via local build/package props so new central governance does not
change its historic dependency model. None resolves WIP, late-cost, Mobile or production identity policies.
