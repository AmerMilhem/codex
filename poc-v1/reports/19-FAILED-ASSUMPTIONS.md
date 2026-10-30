# Failed Assumptions Register — V1

| ID | Assumption | Evidence / actual observation | Impact / disposition |
|---|---|---|---|
| FA01 | PostgreSQL default rounding equals C# default | round(2.5)=3 vs ToEven=2; decimal vectors | Rejected. Explicit round_even policy and quantization required; legal rounding remains unverified |
| FA02 | .NET decimal safely holds every intermediate product | 24-digit ×24-digit fixed values overflow; exact numerator48 digits | Rejected. Rational/BigInteger intermediate + bounded final conversion; no decimal-only multiplication policy |
| FA03 | Estimated partial cost can never make WIP negative | estimate12, actual10, partial receipts yield -40 then-200 | Rejected. Manufacturing blocked for financial policy approval |
| FA04 | Forbid credit WIP without changing schedule or partial values | funded-estimate receipt30 rejected with WIP320<360 | Rejected. Cap/deferral/control policy must be explicit |
| FA05 | Worker lease fencing prevents all duplicate deliveries | crash after consumer commit creates second delivery, one effect | Rejected. At-least-once + Inbox, no exactly-once claim |
| FA06 | Valid ID token implies ERP permission/tenant claim trustworthy | B tenant_hint ignored; removed membership/disabled→403 | Rejected. Current ERP grants required |
| FA07 | Available official source metadata proves downloadable runnable artifacts | PG18.5 manifest unknown; Flutter archive403 | Rejected. PoC PG18.2 digest verified; production patch / mobile pair pending |
| FA08 | Quartz3 bootstrap/job signatures apply unchanged to Quartz4 | StdSchedulerFactory missing; Execute requires CancellationToken | Rejected. Corrected using installed package's XML docs and QuartzSchedulerBuilder; runtime probe passed |
| FA09 | Seeding reservation balance alone is sufficient for all-ledger reconciliation | Earlier fixture had100 stock rows without opening facts | Rejected. Opening stock/value/GL entries added; strict all-pool restore reconciliation now zero mismatches |
| FA10 | Successful local backup establishes cloud DR/SLA | local snapshot restored; keys/IdP/PITR not exercised | Not established. Cloud operations/release gate pending |

Harness fixes retained in evidence: initial incorrect big-numerator threshold (10^50 vs true48digits), changing
HttpClient.Timeout after use, missing OIDC iat in fixture, asynchronous Angular DOM check, and existing dev-server
port during npm ci. These were corrected and rerun, not silently counted as successful first runs.
No known remaining failing executed checks. Unexecuted scenarios remain PENDING/CONDITIONAL in their reports.
