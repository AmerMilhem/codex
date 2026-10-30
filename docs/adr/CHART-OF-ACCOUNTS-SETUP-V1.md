# ADR: Controlled company account creation

**Status:** Implemented for Vertical Slice #7.

Finance owns account creation through a scoped, permission-checked command. The existing
accounting-book row serializes creation for one company; application and insert-trigger
checks compare canonical codes, including legacy mixed-case codes. The existing unique
index protects exact codes. Code normalization is uppercase ASCII with trimmed outer
whitespace and no interior whitespace. Names remain a single Unicode field because the
approved model has no translated-name structure.

The migration adds nullable creator metadata so historical accounts retain their IDs,
codes, state and financial links without invented actors. A deferred database trigger
requires creation audit/outbox effects at commit for runtime-created rows. Existing FORCE
RLS, company book FK and independent Account View/Create permissions remain in force.
The migration was generated through the fresh-build wrapper introduced after the Fiscal
Period Setup EF incident; automatic Down is disabled for creation history.
