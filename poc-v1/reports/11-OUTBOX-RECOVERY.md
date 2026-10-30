# Outbox Recovery Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
إثبات التعافي مع at-least-once وconsumer idempotency.

## Hypothesis
lease bounded + stable event id + Inbox/effect atomic تعالج crash/duplicates؛ fence يرفض ACK القديم ولا يمنع duplicate network send.

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
حدث مالي durable؛ عاملان؛ lease tokens؛ child worker killed before and after consumer commit؛ consumer hash collision؛ HTTP timeout stub.

## Steps
اقتل عمليتين فعليًا قبل dispatch وبعد consumer commit؛ سرّع lease expiry صراحة؛ أعد claim؛ نفذ duplicate consumer ثم ACK fenced. اختبر DB timeout وsend timeout.

## Expected Result
يتكرر التسليم عند الحاجة؛ consumer effect واحد؛ لا claimين لlease حية؛ old ACK zero rows؛ poison payload conflict visible.

## Actual Result
عاملان قُتلا فعليًا؛ 3claims و2deliveries و1consumer effect. نموذج HTTP أنتج sendين وأثرًا واحدًا. Inbox rollback أزال marker مع effect. DB timeout ثم fresh scoped worker نجح.

| Test | Result | ms |
|---|---|---:|
| `outbox-real-worker-process-kill-before-and-after-consumer` | PASS | 470.02 |
| `outbox-crash-before-send-after-send-lease-fencing` | PASS | 23.15 |
| `two-workers-skip-locked-one-pickup` | PASS | 11.76 |
| `consumer-inbox-and-effect-atomic-rollback` | PASS | 3.46 |
| `database-command-timeout-and-worker-restart` | PASS | 1007.05 |
| `external-timeout-retry-stable-event-id` | PASS | 698.02 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
consumer في crash harness معاملة DB مستقلة، وHTTP provider stub منفصل؛ لا ادعاء تنفيذ provider حقيقي. poison hash مرفوض؛ circuit breaker/backoff/dead-letter UI كامل وQuartz persistent clustering غير منفذة.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
اعتماد at-least-once مع idempotent consumer/stable external key فقط. لا exactly-once. retry غموض مزود غير idempotent يحتاج query/reconciliation لا blind resend.
