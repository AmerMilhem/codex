# Migration Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
اختبار expand→mixed-version compatibility→backfill→contract.

## Hypothesis
nullable expansion + dual-compatible writer + bounded backfill تسمح بتعايش N وN+1 قبل retirement المنضبط.

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
3 صفوف؛ old_name→new_name؛ كتابة N أثناء backfill وكتابة N+1.

## Steps
أضف column، trigger يحافظ على التوافق في الاتجاهين، اقرأ COALESCE؛ backfill صفحة1؛ تحقق بلا mismatches؛ validate NOT NULL؛ أوقف N writers منطقيًا ثم contract.

## Expected Result
كل إصدار يقرأ كتابة الآخر؛ race لا يفقد البيانات؛ contract بعد N retirement فقط.

## Actual Result
{"rows": 3, "expandMs": 1.0749, "consecutiveVersionsCompatible": true, "backfillMismatches": 0, "contract": "only after N writers retired", "limitation": "small local fixture; no fleet-scale lock/replication proof"}

| Test | Result | ms |
|---|---|---:|
| `expand-dual-compatible-backfill-contract` | PASS | 14.91 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
N/N+1 ممثلان باستعلامين وليس نشر OCI rolling فعلي؛ fixture صغير، لا long-running transaction/replica/fleet proof. Contract backward-incompatible عمدًا بعد retirement؛ ليس rollback عامًا ممكنًا دائمًا.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
اجعل expand/contract releases منفصلة؛ controlled migration runner وlock_timeout ومراجعة fleet compatibility شرط تنفيذ الترحيل الإنتاجي.
