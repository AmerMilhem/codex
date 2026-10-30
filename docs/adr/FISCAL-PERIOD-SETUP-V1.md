# ADR: Controlled fiscal-period creation

**Status:** Implemented for Vertical Slice #6.

Create periods through a Finance command with an independent permission and verified
company context. The existing company accounting-book row is the serialization point:
the application locks it exclusively, then checks inclusive overlap; the period insert
trigger performs the same guarded check. This reuses the existing book and period index
without introducing calendar templates or another persistence aggregate.

The migration adds nullable creator metadata so old Open and Closed rows retain their
exact historical values and do not acquire a fictional creator. New runtime inserts must
have creator metadata and two audit actions plus one versioned outbox event at commit.
The existing FORCE RLS policy and scope FK remain in force. Legacy administrator-owned
test fixtures can retain preexisting rows without new creation metadata; production
runtime uses the guarded command.

The historical EF migration-generation incident and the preflight wrapper are recorded
in [incident evidence](../evidence/period-setup-v1/MIGRATION-INCIDENT.md).
