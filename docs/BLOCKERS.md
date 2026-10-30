# Known blockers and stage gate

Still blocked: Manufacturing business features/final WIP policy, multi-source late-cost propagation, full AR/AP allocations/FX,
complex reservations, production managed OIDC/MFA, Flutter/mobile, JoFotara production integration, cloud production deployment
and cloud PITR/DR/SLA claims. Jordan legal/retention/residency/transfers remain Pending Verification.
No Kubernetes/Kafka/independent broker/distributed cache/OpenSearch/warehouse/microservice extraction was added.

Before production: choose/test real identity provider and session/MFA policy, cloud/provider-supported current servicing patch,
exporter/collector/redaction configuration, production secret/KMS adapters and recovery/compliance controls. Existing18.2 DB image
is a reproducible dev/test pin, not production patch approval. CI remote execution awaits a reviewed push; local execution of its
commands is recorded separately.

The user authorized **Manual Journal Entry — Draft → Validate → Post**, **Trial Balance — Read Only**, **General Ledger Detail — Read Only**, **Manual Journal Reversal**, **Fiscal Period Close**, **Fiscal Period Setup**, **Chart of Accounts Setup**, **Accounting Book Setup**, and **Manual Journal Correction**. No AR/AP/FX,
Inventory, Manufacturing, fiscal integration or bank/external financial effects were added. Account/period configuration has
minimum persisted models and explicit development fixtures; general administration screens, full fiscal-close workflow and
partial reversal, reversal chains and general correction workflows are outside this slice. Draft deletion/cancellation has no approved policy and is not exposed.
Fiscal Period Close, controlled Fiscal Period Setup and Chart of Accounts Setup V1 are now authorized and implemented;
Accounting Book Setup V1 is authorized and implemented with one book per company;
parallel books, secondary ledgers and book deletion remain outside scope.
period reopening, edits/deletion of used periods, year-end entries/carry-forward, complex
calendar templates and additional close workflows remain outside scope.
General Ledger Detail is now authorized and implemented as a Finance read model with Trial Balance drill-down. Comparative periods, budgets, dimensions, export, projections and a reporting warehouse remain outside scope.

At slice completion stop. A next vertical slice requires separate user approval. A source/test slice gate does not approve
production identity, deployment, legal rounding, localization compliance or the existing pending operational decisions above.

Manual Journal Correction V1 is implemented as an atomic reversal plus replacement command. Correction of correction chains, partial-line correction and corrections in AR/AP, Inventory or Manufacturing remain outside scope.
