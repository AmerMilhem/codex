# EF/Npgsql Transaction Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
إثبات وحدة المعاملة المحلية عبر Business + Finance + Integration contexts.

## Hypothesis
تستطيع ثلاثة DbContexts المشاركة في اتصال ومعاملة واحدة؛ SaveChanges لا يثبت جزءًا مستقلًا.

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
Invoice 123.456789، AR item، journal/lines، audit، outbox؛ مصدر فريد، فترة مفتوحة ومغلقة.

## Steps
ابدأ NpgsqlTransaction؛ اضبط scope محليًا؛ UseTransaction لكل context؛ احفظ بالتتابع ثم commit. كرر rollback، unique failure، terminate backend وdeadlock.

## Expected Result
إما جميع الآثار أو لا شيء؛ 23505/40P01/connection interruption لا تترك نصف عملية.

## Actual Result
نجحت المشاركة الفعلية، rollback بعد SaveChanges، وفشل unique، وقطع الاتصال. كشف PostgreSQL ضحية deadlock واحدة؛ نجحت معاملة جديدة بترتيب أقفال ثابت.

| Test | Result | ms |
|---|---|---:|
| `ef-three-contexts-one-acid-commit` | PASS | 647.28 |
| `ef-savechanges-unique-failure-outer-rollback` | PASS | 12.47 |
| `explicit-rollback-after-savechanges` | PASS | 4.10 |
| `connection-interruption-rolls-back-and-fresh-uow-retries` | PASS | 18.94 |
| `postgres-deadlock-detected-and-full-uow-retry` | PASS | 1012.91 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
EF automatic execution strategy retry غير مفعّل. تجربة retry أعادت UoW صراحة؛ unknown commit يُحسم عبر receipt. لا توزيع معاملات ولا سياقات متوازية على نفس الاتصال.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
اعتماد هذا النمط كأساس تجريبي؛ يجب تكرار الاختبار بعد أي تغيير provider، pooling/proxy أو transaction orchestration.
