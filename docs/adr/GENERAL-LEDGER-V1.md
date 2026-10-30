# ADR — General Ledger Detail V1

Status: implemented within the user-authorized Finance read-only slice, 2026-10-20.

1. **Dedicated Finance query/port/adapter.** Reuse the approved PostgreSQL/Npgsql stack. Explicit SQL makes ordered NUMERIC windows and pre-pagination calculation reviewable. No Dapper, new shared Reporting abstraction, or second ledger.
2. **Lifetime opening, inclusive PostingDate movement.** Match Trial Balance exactly. No implicit fiscal reset or filter on closed periods. One company functional book/currency inherited from the approved baseline.
3. **Signed exact balances.** Positive is debit, negative credit. NUMERIC-to-text at the boundary and scaled BigInteger reconciliation preserve aggregation without float/double monetary calculations. Angular displays server amounts.
4. **Unique deterministic order.** PostingDate, JournalId, Position, LineId ascending. Position disambiguates repeated account lines in one journal. No ordering by mutable reference/name or CreatedAt.
5. **Window before pagination.** OFFSET/LIMIT 1–200 rows with explicit full-period totals. Existing account index supports scoped access. 102k-line measured fixture has 26k period rows on the target account. Retain query plans and costs before considering any extra index.
6. **One repeatable read-only snapshot per request.** Metadata, opening, count, movements and independent TB projection see the same committed source. Read-only enforced by PostgreSQL. Transaction-local context prevents pool leakage. New clicks read fresh committed data; no durable cross-request snapshot is promised. UI explains live behavior.
7. **Actual Trial Balance projection for independent reconciliation.** Do not compare GL output to a second copy of its own totals. Check exact opening/movement/closing plus running equations; expose mismatches with stable codes, warning telemetry and visible UI. No automatic repair.
8. **Existing Posted Journal navigation.** Drill-down preserves company/account/dates/page and opens existing immutable detail/audit view. Update the previous Trial Balance assertion prohibiting GL links because this user authorization now explicitly requires read-only GL links; retain its prohibition on editing/posting.
9. **No schema change.** Existing ownership, FORCE RLS, constraints, indexes, precision and immutable ledger remain the baseline. Full migrations/upgrade, posting/idempotency/outbox and architecture tests remain required.

Acceptance evidence: [General Ledger V1](../evidence/general-ledger-v1/ACCEPTANCE.md).
