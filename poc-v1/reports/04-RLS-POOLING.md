# Tenant RLS / Pooling Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
عزل tenant/company دون الاعتماد على query filter، بما يشمل reuse/cancel/rollback والعمال.

## Hypothesis
RLS مع verified transaction-local context وهوية غير مالكة يفشل مغلقًا ولا يحتفظ بسياق A للطلب B.

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
Tenant A/C1، A/C2، B/C3؛ pool مستقل MaxPoolSize=1 لإثبات نفس backend PID.

## Steps
اقرأ A؛ أعد الاتصال للـpool؛ افتح B على PID نفسه؛ اقرأ دون context؛ جرّب write خارج النطاق وresource ID معدّل؛ ألغِ pg_sleep وأعد التنفيذ كعامل.

## Expected Result
لا صفوف أو writes خارج scope؛ missing context صفر/رفض؛ لا دعم إداري ضمني.

## Actual Result
{"pidA": 203, "pidB": 203, "physicalReuse": true, "afterRollback": 0, "afterCancellation": 0, "worker": "scoped", "support": "no implicit bypass"}

| Test | Result | ms |
|---|---|---:|
| `runtime-role-is-not-owner-or-bypass` | PASS | 21.62 |
| `rls-tenant-company-and-missing-context` | PASS | 18.93 |
| `rls-cross-scope-writes-and-resource-id` | PASS | 14.92 |
| `rls-pool-reuse-rollback-cancel-worker-support` | PASS | 130.22 |
| `malicious-context-parameter-fails-closed` | PASS | 2.45 |
| `mass-assignment-field-and-upload-contract-rejections` | PASS | 25.76 |
| `posted-history-update-delete-denied` | PASS | 3.20 |
| `replay-rechecks-current-authorization` | PASS | 0.49 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
نتيجة RLS تقتصر على تطبيق موثوق يختار scope بعد التفويض. custom GUC قابلة للتغيير من SQL؛ ليست اعتمادًا لهوية العميل ولا حماية ضد SQL injection/اختراق العملية. PgBouncer، prepared-query matrix وsupport approval workflow الكامل غير منفذة.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
يمكن بدء persistence foundation بهذا الضبط، مع رفض serving traffic إذا كانت الهوية owner/BYPASSRLS. احتفظ باختبار الـPID في CI.
