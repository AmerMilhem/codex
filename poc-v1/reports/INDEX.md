# PoC & Technical Validation Sprint — V1

تم تنفيذ تجارب مؤقتة داخل المستودع، دون بناء منتج ERP أو إنشاء موارد مدفوعة.
**القرار الوحيد: CONDITIONAL GO.** نجح 46 اختبار Backend و11 فحص متصفح، والاستعادة المحلية؛ لا إخفاقات متبقية في الاختبارات المنفذة.

التفاصيل والنطاق المسموح باقتراحه والـBlockers في [Readiness Scorecard](22-READINESS-SCORECARD.md).
نجاح الاختبارات المحددة لا يعني اكتمال جميع السيناريوهات الإنتاجية. التصنيع/late-cost graph، الهوية المُدارة،
الموبايل والاستعادة السحابية بقيت مشروطة أو Pending كما هو موثق.

| Requested output | Artifact |
|---|---|
| 01-COMPATIBILITY | [01-COMPATIBILITY.md](01-COMPATIBILITY.md) |
| 02-LICENSES | [02-LICENSES.md](02-LICENSES.md) |
| 03-EF-TRANSACTIONS | [03-EF-TRANSACTIONS.md](03-EF-TRANSACTIONS.md) |
| 04-RLS-POOLING | [04-RLS-POOLING.md](04-RLS-POOLING.md) |
| 05-INVENTORY-CONCURRENCY | [05-INVENTORY-CONCURRENCY.md](05-INVENTORY-CONCURRENCY.md) |
| 06-IDEMPOTENCY | [06-IDEMPOTENCY.md](06-IDEMPOTENCY.md) |
| 07-ACCOUNTING | [07-ACCOUNTING.md](07-ACCOUNTING.md) |
| 08-DECIMAL-ROUNDING | [08-DECIMAL-ROUNDING.md](08-DECIMAL-ROUNDING.md) |
| 09-MOVING-AVERAGE | [09-MOVING-AVERAGE.md](09-MOVING-AVERAGE.md) |
| 10-WIP-PARTIAL-RECEIPT | [10-WIP-PARTIAL-RECEIPT.md](10-WIP-PARTIAL-RECEIPT.md) |
| 11-OUTBOX-RECOVERY | [11-OUTBOX-RECOVERY.md](11-OUTBOX-RECOVERY.md) |
| 12-OIDC | [12-OIDC.md](12-OIDC.md) |
| 13-ANGULAR | [13-ANGULAR.md](13-ANGULAR.md) |
| 14-FLUTTER | [14-FLUTTER.md](14-FLUTTER.md) |
| 15-MIGRATION | [15-MIGRATION.md](15-MIGRATION.md) |
| 16-BACKUP-RESTORE | [16-BACKUP-RESTORE.md](16-BACKUP-RESTORE.md) |
| 17-SECURITY-NEGATIVE | [17-SECURITY-NEGATIVE.md](17-SECURITY-NEGATIVE.md) |
| 18-RECONCILIATION | [18-RECONCILIATION.md](18-RECONCILIATION.md) |
| 19-FAILED-ASSUMPTIONS | [19-FAILED-ASSUMPTIONS.md](19-FAILED-ASSUMPTIONS.md) |
| 20-UPDATED-ADRS | [20-UPDATED-ADRS.md](20-UPDATED-ADRS.md) |
| 21-UPDATED-RISKS | [21-UPDATED-RISKS.md](21-UPDATED-RISKS.md) |
| 22-READINESS-SCORECARD | [22-READINESS-SCORECARD.md](22-READINESS-SCORECARD.md) |

Additional: [failure injections](23-FAILURE-INJECTION.md)، [raw final results](../evidence/results.json)،
[source manifest](../evidence/official-source-manifest.json)، [full licenses](../evidence/resolved-license-matrix.csv).
[Cloud environment setup / saved draft](ENVIRONMENT-SETUP.md).

Reproduce: `python3 scripts/bootstrap.py`, `bash scripts/run.sh`, `bash scripts/web.sh`, then
`python3 scripts/reports.py`. Use only disposable local services; run.sh resets `poc` on its named test container.
Frozen dependency locks are included. Evidence records the exact run time and environment; logs from failed earlier
harness revisions are preserved separately and never counted toward the final passed checks.

Approval record: the uploaded task explicitly approved the Blueprint and authorized only this PoC sprint.
No implementation started. Proposed next stage: **Implementation Foundation — V1 (Backend/Web), with explicit exclusions**.
Await user approval before creating production foundation or product features.
