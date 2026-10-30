# Manual Journal implementation decisions — V1

| ID | Decision | Reason / scope |
|---|---|---|
| MJ01 | JournalEntry aggregate owns JournalLines; Draft/Posted only | Actual business lifecycle; Validate is not a status |
| MJ02 | Posted journals/lines form the immutable GL fact for this slice | Avoid two financial sources of truth; future reports read these facts |
| MJ03 | One functional-currency book policy per company, NUMERIC(24,6), decimal strings, currency minor-unit validation | Prior decimal/internal-ToEven policy; no FX or inferred legal rounding; reject silent rounding |
| MJ04 | Root row lock + strong revision, shared reference locks, transactional company/year counter | Serialize edit/post, closure/disable and number allocation without silent overwrite |
| MJ05 | Post uses Foundation scoped idempotency receipt in the same transaction as journal/audit/outbox | Replay after unknown commit; permanent posted state prevents re-post with a new key |
| MJ06 | Independent Finance.Journal.View/Create/Edit/Post; Validate uses Edit | Creation/editing does not grant financial commitment; no invented approval workflow |
| MJ07 | Finance-owned migrations/history and mandatory runtime schema/RLS guard | Preserve Foundation ownership and existing migration history; no per-replica migration |
| MJ08 | Narrow owner lock functions, fixed search_path + explicit scope + FORCE RLS | PostgreSQL row locking otherwise requires runtime UPDATE on reference data; preserve read-only grants |
| MJ09 | Versioned minimal event plus Inbox-deduplicated audit delivery consumer | Prove real durable trace without creating another posting or implementing another business module |
| MJ10 | Explicit dev seed and memory-only verified local Web session | Make the authorized UI usable while preserving pending production IdP/MFA gate |
| MJ11 | No draft deletion/cancel, reversal, full CoA/period admin or closing workflow | No approved deletion policy; capabilities beyond minimum model are not invented |
| MJ12 | Reconciliation reports per-journal and global scoped mismatch; never fixes history | Failure remains visible for investigation/correction under future approved workflow |

Implementation refinements: ScopeAction metadata moved from host to shared delivery plumbing to avoid a Finance→host
dependency. Accounts/periods are minimum persisted reference models projected into pure domain snapshots. Finance Down is
blocked to protect financial history. Application-assigned UUIDs are explicitly ValueGeneratedNever so new child lines are
INSERTs, not mistaken EF UPDATEs. Separate Finance history prevents changing the existing Foundation context's migration graph.
No technology-stack change, package version change, legal-policy approval or additional business slice is implied.

Browser inspection also exposed Foundation's Angular critical-CSS inlining conflicting with its strict CSP. Disable
`optimization.styles.inlineCritical` so CSS loads as an external same-origin file; retain the CSP without unsafe-inline.
Both shell and Finance browser suites now verify computed styles, along with narrow-viewport containment.
