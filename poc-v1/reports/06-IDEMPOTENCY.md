# Idempotency Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
حماية posting intent من إعادة الطلب والمهلة وفقدان الاستجابة.

## Hypothesis
مفتاح ضمن tenant/company/caller/command/target وبصمة تشمل original revision يحفظ receipt واحدًا، مع source uniqueness مستقلة.

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
Invoice 100، X، eight contenders؛ timeout caller؛ طفل يُقتل بعد commit وقبل response.

## Steps
أرسل مرة وتسلسليًا ومتزامنًا، وبمحتوى مختلف. اقتل العملية بعد durable marker ثم أعد نفس intent. افحص journal/outbox/document والرفض بعد سحب التفويض.

## Expected Result
Same key/same fingerprint returns same id; different fingerprint conflict; changed key لا يعيد source effect. لا invoice stock effect أصلًا؛ إصدار المخزون فعل منفصل.

## Actual Result
كل الحالات نفذت بأثر محلي واحد. اختبار فقد الاستجابة قتل عملية فعلية. unique business source منع تكرار posting بمفتاحين.

| Test | Result | ms |
|---|---|---:|
| `same-key-once-sequential-concurrent-timeout-replay` | PASS | 37.30 |
| `same-key-different-fingerprint-rejected` | PASS | 2.56 |
| `process-killed-after-commit-replayed-once` | PASS | 859.15 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
Client timeout الأول هو WaitAsync في harness وليس رحلة HTTP كاملة؛ process-kill يغطي committed/no response. لا إثبات exactly-once لإرسال خارجي؛ outbox/inbox منفصلان. لم تثبت سياسة key retention أو eviction زمنية.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
احتفظ بuniqueness دائمة للأثر؛ لا تعتمد على TTL وحدها. أعد current authorization قبل receipt replay.
