# Inventory Reservation Concurrency Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
مقارنة صحة سباق الحجز مع استراتيجيات PostgreSQL متعددة.

## Hypothesis
لا يمكن حجز 8 و7 معًا من Available=10؛ reservation events وreserved projection يلتزمان بالمعاملة نفسها.

## Environment
Debian 13 / Linux x64، Docker محلي، PostgreSQL disposable؛ بيانات اصطناعية. لا موارد سحابية مدفوعة.
هوية تشغيل قاعدة البيانات `poc_runtime` ليست owner/superuser/BYPASSRLS. جميع السياقات محلية للمعاملة.

## Versions
راجع [المصفوفة المثبتة](01-COMPATIBILITY.md)، [runtime versions](../evidence/runtime-versions.json)،
[NuGet lock](../src/packages.lock.json) و[npm lock](../web/package-lock.json).

## Configuration
المخطط الأدنى [schema.sql](../src/schema.sql)، المنفذ [Program.cs](../src/Program.cs)، وإعادة التنفيذ
`bash scripts/run.sh` من جذر `poc-v1`. قاعدة التجربة تُعاد تهيئتها؛ لا تستخدم اتصالًا ببيانات حقيقية.

## Test Data
25 سباقًا لكل استراتيجية، 100 سباق إجمالًا؛ لكل منتج opening ledger 10/value100 وGL، لا balance seed بلا ledger.

## Steps
حرر مهمتين معًا، احجز بالـatomic update/row lock/Serializable/optimistic revision. أعد UoW حتى 3 مرات للأخطاء القابلة للإعادة، وارفض عدم الكفاية.

## Expected Result
فائز واحد في كل سباق؛ reserved=7 أو8، never15؛ projection=reservation events؛ stock/value=ledgers/GL.

## Actual Result
100 سباق: صفر double winners؛ stale revision أثّر في صفر صف. راجع المقارنة المقاسة أدناه.

| Test | Result | ms |
|---|---|---:|
| `reservation-8-vs-7-atomic` | PASS | 234.49 |
| `reservation-8-vs-7-row-lock` | PASS | 345.24 |
| `reservation-8-vs-7-serializable` | PASS | 454.79 |
| `reservation-8-vs-7-optimistic` | PASS | 415.03 |
| `stale-concurrency-token-does-not-overwrite` | PASS | 1.99 |

| Strategy | Races | Double success | Retries | Deadlocks | Winner p95 ms |
|---|---:|---:|---:|---:|---:|
| atomic | 25 | 0 | 0 | 0 | 1.643 |
| row-lock | 25 | 0 | 0 | 0 | 9.589 |
| serializable | 25 | 0 | 25 | 0 | 5.913 |
| optimistic | 25 | 0 | 25 | 0 | 8.293 |

## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
pg_sleep/Task.Delay القصير مقصود لتوسيع نافذة السباق، لذلك الأرقام لا تعطي ranking إنتاجيًا محايدًا ولا throughput capacity. تعدد المنتجات/المخازن وrelease/expiry وإدخال bucket غائب تظل حالات لاحقة قبل reserve production feature.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
Atomic conditional update مناسب للحجز الأحادي البسيط؛ row locks مع ترتيب ثابت للعمليات متعددة الموارد. لا تفرض Serializable افتراضيًا على كل العمليات. صحة Ledger أهم من فرق الميلي ثانية.
