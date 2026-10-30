# Migration generation incident — Fiscal Period Setup V1

During Setup migration generation, `dotnet ef migrations add --no-build` used a compiled
model that preceded the current source changes and produced an empty Setup migration.
Running `dotnet ef migrations remove --no-build` against that stale assembly then removed
the prior `20261023152528_FiscalPeriodCloseV1` source migration and reverted the Finance
model snapshot, while the empty Setup files remained in the source tree. The affected
files were the Close `.cs` and `.Designer.cs`, the Finance snapshot, and the empty Setup
`.cs`/`.Designer.cs` pair.

The empty Setup files were removed. The Close migration operations were reconstructed,
its Designer was restored from the generated model file, and the snapshot was restored
from that model. The restored Close Designer SHA-256 matches the previously retained
Slice #5 source manifest exactly. The Close migration operation file has a different
SHA-256 because its formatting/comments were reconstructed; its schema operations and
security SQL are tested, not asserted byte-identical. After compiling the Finance project,
EF generated the nonempty `20261023212645_FiscalPeriodSetupV1` migration and current
snapshot. No applied production database was modified during this incident.

Verification explicitly checks four migration IDs in order, a clean install, sequential
upgrade through the Close migration to Setup, pending-model-change status, final schema
constraints/functions/FORCE RLS, and preservation of prior Open/Closed periods, Close
actor/time/comment/reconciliation, and a balanced Posted journal with its period FK.
The full regression gate also exercises earlier Finance slices against the final schema.

To prevent a repeat, `scripts/add-finance-migration.sh` compiles Finance immediately before
`dotnet ef migrations add --no-build`. Do not run `dotnet ef migrations remove --no-build`
against an unverified assembly. Review the generated migration operations and migration
order before running any downgrade command. Finance history has no automatic Down path.
