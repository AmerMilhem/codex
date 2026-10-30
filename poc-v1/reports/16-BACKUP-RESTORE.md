# Backup / Restore Report

**Result: CONDITIONAL** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
إثبات restore محلي مستقل ومصالحة بعده بدل اعتبار dump نجاحًا كافيًا.

## Hypothesis
pg_dump18.2→isolated DB restore يعيد محتويات كل الجداول وGL/stock/value؛ process restart يحافظ على facts.

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
19 جداول؛ synthetic private object؛ original poc preserved وpoc_restore أعيد إنشاؤها محليًا.

## Steps
python3 scripts/backup_restore.py: dump، fingerprint كل table، restore --exit-on-error، قارن counts/hash، reconcile، احذف مصدرobject-fixture ثم استعده، أوقف/أعد DB محليًا وافحص availability والمحتوى.

## Expected Result
تطابق كامل؛ zero GL/stock/value mismatches؛ object SHA256 نفسه؛ restart يعيد نفس facts.

## Actual Result
PASS local: 19 tables؛ GLdiff=0.000000، stock=0، valuation=0. dump 0.145s، restore+check 1.851s، restart+verify 1.624s.



[Full before/after fingerprints](../evidence/backup-restore.json).

## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
هذه أزمنة fixture لا RPO/RTO. Logical snapshot بدون WAL/PITR. role reused في cluster نفسه؛ KMS/managed object versions/IdP config/provider already-accepted outcomes غير مستعادة. خدمة cloud/region لم تعتمد بعد.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
Local approach validated؛ **Cloud restore/SLA readiness remains pending**. لا تدّع end-to-end disaster recovery أو تنشئ موارد مدفوعة دون اعتماد منفصل.
