# ADR — Manual Journal Reversal V1

**Status:** Accepted

1. **Separate Posted journal.** Reversal creates an ordinary balanced Posted journal with exact persisted debit/credit inversion.
   The original is never mutated, and no new journal status is introduced.
2. **Immutable explicit relationship.** One scoped relation links original and reversal in both directions and retains operational
   reason/date/actor/request/correlation metadata. Unique keys forbid two reversals and reusing a reversal.
3. **Independent allowed date.** The original period may be Closed; the new reversal date must resolve to one Open company fiscal
   period. No historical date or period is rewritten.
4. **Single transaction.** Journal, numbering, relationship, audit, outbox and idempotency receipt commit together. A deferred
   database guard verifies exact opposite lines and required evidence before commit.
5. **Ordinary ledger reporting.** Trial Balance and General Ledger aggregate both journals from the Posted ledger. No special
   report adjustment, suspense entry, projection or mutable balance is introduced.
6. **Bounded correction model.** Partial reversal, reversal of a reversal, chains, scheduled reversal and wider correction workflows
   remain outside this slice. A future correction must be a separately approved business command.
