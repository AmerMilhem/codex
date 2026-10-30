# Updated ADR Register — PoC V1

This register appends evidence; it does not retroactively edit approved V1 baselines or self-approve new financial policy.
Statuses: **Validated for tested PoC scope** or **Proposed / Pending approval**, not blanket production Approved.

| ADR | Context / decision | Status | Evidence | Alternatives / consequences / revisit |
|---|---|---|---|---|
| POC-01 | One Npgsql local transaction explicitly shared by module DbContexts | Validated for tested scope |03,07| Separate local SaveChanges commits rejected; rerun on provider/pool changes |
| POC-02 | Transaction-local tenant/company RLS, nonowner runtime, no missing-scope access | Validated for tested scope |04,17| App-filter-only rejected; GUC not identity, PgBouncer needs own proof |
| POC-03 | Atomic conditional update for simple one-bucket reserve; ordered locks for multi-resource plan | Proposed implementation detail |05| Serializable/optimistic correct but retried; no throughput extrapolation |
| POC-04 | API receipt scope/fingerprint + permanent source-effect uniqueness | Validated for tested scope |06,07| TTL alone insufficient; external effect requires its own idempotency/reconciliation |
| POC-05 | Explicit quantization and bounded rational intermediates | Proposed financial numeric policy, technically validated |08| default PG round/decimal-only arithmetic rejected; legal overrides remain Pending Verification |
| POC-06 | Base MWA from locked Q/V, final issue residual, immutable corrections | Validated base; downstream method Pending |09| no average field as truth; multi-source graph/price adjustment still blocked |
| POC-07 | No final partial-production cost policy selected | Pending finance approval |10| estimate with controlled credit vs cap vs receipt deferral; manufacturing blocked |
| POC-08 | Outbox at-least-once + stable ID + atomic inbox/effect + fenced ACK | Validated for tested scope |11| independent broker not needed for demonstrated cases; no exactly-once claim |
| POC-09 | BFF code+PKCE, ERP grants separate from IdP | Conditional |12,17| managed IdP/MFA/rotation/TLS still required; no production auth readiness |
| POC-10 | Angular22.2.3 + TS6.0.3 + Node24.19.0; native HTML grid first | Validated technical feasibility |01,13| no commercial component adopted; a11y/browser scale gates before product UI |
| POC-11 | Flutter exact SDK/Dart pair remains unselected | Pending |01,14| no Flutter code before gate; mobile launch criticality OPEN |
| POC-12 | Expand/backfill/contract after N retirement; isolated restore rehearsal | Validated local / cloud Conditional |15,16| no automatic rollback promise after contract; no cloud SLA until cloud drill |
| POC-13 | Quartz4 bootstrap differs; RAM trigger compatible | Validated compatibility only |01,compat test| persistent store/clustering remain pending; timing is not business authority |
| POC-14 | Local OTel export works; managed collector pending | Validated compatibility only |01,compat test| no unaudited PII/secrets export; exporter/redaction review before operations |

Binding approved principles retained: modular monolith/local ACID; company/product/book MWA;
immutable posted ledgers; scoped owner ports; no independent broker/Kubernetes/cache introduction.
All compliance decisions listed in previous baseline remain Pending Verification.
