# ADR: Controlled fiscal-period close

**Status:** Implemented for Vertical Slice #5.

Use a dedicated Finance command and independent `Finance.Period.Close` permission.
The period row is the aggregate root and lock target. The command repeats pre-close
TB, posted-ledger and per-account GL checks after acquiring the row lock, then commits
state, audit, outbox and idempotency receipt together. The preview is read only and
never authorizes a later close by itself. No new balance projection or year-end
structure is introduced.

Database constraints enforce Open→Closed metadata, immutable Closed records and
non-overlapping company periods. A deferred close trigger requires two audit actions
and exactly one versioned event. The migration temporarily removes `FORCE ROW LEVEL
SECURITY` inside its transaction so the non-superuser table owner can backfill all
tenants, then restores it before commit. Without this step RLS silently hides old
Closed rows and the new constraint fails. Legacy Closed records receive an explicit
unverified marker, preserving the distinction from verified command closes.

One financial boundary remains deliberately outside this slice: year-end processing
and reopening a Closed period require separate policy and authorization.
