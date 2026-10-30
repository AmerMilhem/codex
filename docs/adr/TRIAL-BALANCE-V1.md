# Trial Balance decisions — V1

| ID | Decision | Reason |
|---|---|---|
| TB01 | Dedicated read query over posted journals/lines only | No mutable balance field, cached source or financial write path |
| TB02 | Lifetime company-book opening, inclusive movement dates, explicit net-side opening/closing | Correct current ledger semantics across fiscal years; no invented annual reset |
| TB03 | Read-only Repeatable Read transaction with transaction-local scope | Reconciliation and projection use one committed snapshot during concurrent posting |
| TB04 | Parameterized SQL/Npgsql with server aggregation and exact numeric strings | Clear independent control query and unbounded aggregate precision; no new dependency |
| TB05 | Measured EF projection as an alternative | Strategy selection is reviewable; no assertion that SQL is universally faster |
| TB06 | Independent controls plus per-journal integrity and per-account equations | Detect projection omissions and globally cancelling errors; surface failures without repair |
| TB07 | Existing Finance.Journal.View and Foundation ProblemDetails | No invented write permission or command semantics for a report |
| TB08 | No schema/index change unless query plans justify it | Existing scoped indexes must be evaluated on broad history and selective dates |
| TB09 | Pure server decimal strings with six-place BigInteger verification | Totals may exceed single-journal/.NET decimal capacity; no silent rounding drift |
| TB10 | No export or GL detail in this slice | Preserve the approved read-only Trial Balance scope |

Compatibility and production identity/legal/deployment approvals are unchanged. Test-only superuser corruption is isolated
to disposable data to prove failure detection; runtime cannot bypass immutable posting guards and no repair API was added.

The complete gate exposed a browser-fixture startup race: the PostgreSQL image's temporary initialization server answers
Unix-socket readiness before the final server starts. Both Finance browser runners now require TCP database readiness before
creating roles. This is a test-environment reliability correction, without changing financial behavior or weakening assertions.
Repeated local runs also exposed retained disposable databases consuming disk. The shared test fixture now auto-removes its
container on stop, and both browser runners remove only their own anonymous test volumes. Verified stopped containers from
this task were cleaned; existing development/PoC databases and their volumes were preserved. No broad Docker prune is used.
