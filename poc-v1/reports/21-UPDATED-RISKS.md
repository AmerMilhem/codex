# Updated Risk Register — PoC V1

| Risk | State / evidence | Severity | Owner | Mitigation / gate |
|---|---|---|---|---|
| R01 Lost local posting effect | Reduced by ACID + deferred effects/balance tests | Critical | Backend/Finance | preserve one UoW, source uniqueness, rerun after persistence changes |
| R02 Cross-tenant/company pooling leak | No leak in tested runtime/reuse/cancel paths | Critical | Security/Platform | deployment role gate, verified scopes, no generic SQL; pool/proxy changes require rerun |
| R03 Overselling reservations | 100 pair races, no double win | Critical | Inventory | ordered multi-resource/expiry/release tests before full reserve feature |
| R04 Undetected decimal mismatch/overflow | Defaults differ; explicit policy vectors pass | Critical | Finance/Backend | bounded rational intermediates and legal rounding review |
| R05 WIP credit policy/uneven output cost | OPEN | Critical | Financial controller/Manufacturing | approve estimate/cap/deferral; manufacturing blocked |
| R06 Multi-source late cost and downstream graph | UNPROVEN | Critical | Costing/Architecture | bounded graph, stale cutoff, concurrency and finance review; affected posting features blocked |
| R07 Real OIDC/MFA/session revocation | Local protocol fixture only | High | Identity/Security | choose managed test tenant; MFA, refresh rotation, outage/TLS browser tests |
| R08 Flutter/Dart/plugins/device capability | Official archive403; no execution | High | Mobile/Platform | allow official host, exact pair/checksum, Android+iOS devices; mobile implementation blocked |
| R09 Cloud restore/keys/PITR/external accepted outcome | Local19-table restore only | Critical before launch | SRE/Security/Integrations | provider/region chosen then paid-resource approval and end-to-end drill |
| R10 Licenses/notices for native/build/mobile/vendor dependencies | Declared installed licenses identified; MPL2/CCBY present | Medium | Legal/Engineering | preserve notices; review distributed artifacts, vendor terms/plugins before adoption |
| R11 Browser/accessibility/scale | Chromium100rows only | Medium | Web/QA | screen reader, keyboard audit, target-browser and large-grid cases |
| R12 AR/AP typed allocation FX/control reconciliation | Numeric/partial debit fixture only | High before feature | Finance/QA | two-ended allocations, overpayment, FX, AP and reversal tests |
| R13 Migration lock/fleet/long transaction | Small local N/N+1 SQL fixture only | High | Platform/Backend | controlled runner, canary, lock budgets, actual rolling proof |
| R14 Current supported production servicing patch | PG18.2 PoC; 18.5 image absent | High before deployment | Platform | select managed supported patch, digest/restore regression; no latest policy |
| R15 Jordan/JoFotara/retention/residency/transfers | Pending Verification unchanged | Critical before legal workflows | Compliance/Local counsel | official evidence and separately approved contracts; no inferred legal signoff |
| R16 Quartz scheduler store + OTLP provider | Quartz RAM/OTel local exporter only | Medium | Platform | real persistent job-store and collector checks before operational claims |

No new production dependency or paid service is authorized by this risk register.
