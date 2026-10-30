# ADR: Atomic manual journal correction

**Status:** Implemented for Vertical Slice #9.

Correction is one idempotent Finance transaction that posts an exact reversal and a
validated replacement before committing a separate three-journal relationship. Reusing
the existing journal posting and reversal validation preserves decimal precision,
account/period/book rules and independent numbering. The original is never mutated.

The relationship is unique and immutable, with tenant/company foreign keys and FORCE RLS.
Existing independent reversal links continue to represent a manual reversal; correction
rejects journals already participating in one. Correction chains and partial-line fixes
are not supported. The migration was generated with the fresh-build EF wrapper, and
automatic downgrade is blocked because it would delete financial correction history.
