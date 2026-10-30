# Security Negative Test Report

**Result: CONDITIONAL** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
إثبات أن محاولات السلبية تفشل، وتحديد مستوى الاختبار لكل حد.

## Hypothesis
RLS/scoped keys + current authorization + strict schemas/DTO fields ترفض الهجمات المحددة.

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
A/B/C1/C2، missing/malformed context، foreign resource IDs، deny after replay، unknown JSON properties، forbidden cost، bad MIME/size/hash.

## Steps
نفذ negative cases في SQL runtime وcontract functions وBFF fixture. valid tokens تحت removed membership وdisabled state لا تصرح.

## Expected Result
cross reads0؛ writes42501؛ modified ID0؛ mass assignment JsonException؛ invalid upload rejected؛ replay authorization denied؛ protected server DTO excludes cost.

## Actual Result
كل negative checks المنفذة نجحت. Contract mass assignment/upload tests هي function-level؛ tenant/company tests DB-level؛ BFF field/auth tests HTTP-level.

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
ليس penetration test. لا full multipart/object scan، full permission/field search/sort/export، real support impersonation approvals أو production OIDC/MFA. generic SQL غير exposed؛ RLS لا يمنع compromised runtime من إعادة ضبط GUC.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
Security foundation مشروط بالمحافظة على الحدود المختبرة. امنع production auth/upload/support features حتى evidence لمسارها الكامل.
