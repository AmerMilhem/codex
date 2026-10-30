# Accounting Posting Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
Business operation تتطلب GL لا يمكن تثبيتها دون الآثار المحلية الإلزامية.

## Hypothesis
قواعد posting ثابتة للتجربة AR→Revenue، مع period/account checks وdeferred balance/source constraints، تحقق atomic posting.

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
فترة مفتوحة/مغلقة؛ حساب صحيح/مفقود/Inactive؛ balanced/unbalanced journals؛ concurrent duplicate source.

## Steps
Post document باستخدام rule v1؛ أنشئ journal/lines، AR وaudit/outbox داخل المعاملة. اختبر commit مباشر لمستند دون ledger، ثم الحالات السلبية.

## Expected Result
Debit=Credit؛ no duplicate source؛ closed/invalid/inactive rejects؛ missing ledger يمنع business commit.

## Actual Result
deferred required-effects trigger رفض المستند بلا GL/AR/Audit/Outbox؛ unbalanced journal ألغى الكل. concurrent duplicate posting نتج عنه أثر واحد.

| Test | Result | ms |
|---|---|---:|
| `posted-business-without-required-accounting-rejected` | PASS | 5.63 |
| `deferred-unbalanced-commit-rolls-back-everything` | PASS | 9.28 |
| `posting-closed-invalid-inactive-accounts` | PASS | 6.77 |
| `fresh-concurrent-key-and-business-source-uniqueness` | PASS | 27.79 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
هذه قاعدتا Debit/Credit ثابتتان وتسجيل rule/event في audit، وليست rule engine إنتاجية أو tax profile أو fiscal clearance. الفترة محمية FOR SHARE؛ race الإغلاق مع reconciliation watermark غير منفذ بعد.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
يسمح بتأسيس engine contracts/local UoW؛ يجب اختبار profiles/dimensions/period-close barrier قبل accounting features الحقيقية.
