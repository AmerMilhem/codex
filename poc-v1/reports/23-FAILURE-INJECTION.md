# Failure Injection Matrix — V1

| Injected fault | Expected state | Actual state / evidence test | Recovery | Duplicate effect? | Manual intervention? |
|---|---|---|---|---|---|
| Process killed after posting commit before response | committed receipt once | process-killed-after-commit-replayed-once PASS | replay same intent | No | No |
| Worker process killed before dispatch | durable pending lease, no consumer effect yet | outbox-real-worker-process-kill-before-and-after-consumer PASS | lease expires; another worker | No | Test accelerates lease clock only |
| Worker process killed after consumer commit before ACK | event redelivered, effect once | same real-process test PASS | duplicate consumer hit inbox | delivery duplicated, effect No | No |
| Backend connection terminated mid-transaction | no business commit | connection-interruption... PASS | new full UoW | No | No |
| PostgreSQL deadlock | one40P01 victim | postgres-deadlock... PASS | fresh UoW consistent lock order | No | No |
| Serializable write contention | 40001 and bounded retry | reservation-serializable PASS; counters in results | restart whole transaction | No double reserve | No |
| DB command timeout | transaction aborted/rollback, scope not reused | database-command-timeout... PASS | rollback; new scoped worker | No | No |
| Database stopped | connection unavailable, committed facts retained | backup-restore databaseOutageObserved=true | start own disposable DB; verify all fingerprints | No | Local test controls service lifecycle |
| Duplicate command / changed fingerprint | replay / conflict | idempotency tests PASS | same receipt / reject conflict | No | No |
| External API timeout after effect | outcome may be unknown | external-timeout... PASS | stable provider key, retry idempotent stub | two sends, one effect | Real nonidempotent provider may need reconciliation |
| Stale lease ACK | zero rows | outbox-crash... PASS | current lease ACK only | No | No |
| Stale revision | zero update | stale-concurrency-token... PASS | explicit conflict, never auto-overwrite intent | No | No |
| Consumer payload hash conflict | visible reject | inbox.payload_conflict expected in outbox test | investigation; no silent accept | No extra effect | Yes if real incident |
| Unbalanced/required accounting missing | whole UoW rejected | accounting tests PASS | correct intent/config then new allowed attempt | No | User/config intervention needed |

Not executed: actual network partition during COMMIT ACK (process-kill after commit is the exercised ambiguity window),
managed IdP outage/rotation, cloud-zone failure, real provider accepted-state replay after backup restore.
These remain pending; the table does not substitute modeled lease expiry for physical crashes where not performed.
