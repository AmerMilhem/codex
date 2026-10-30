# Moving Weighted Average Report

**Result: CONDITIONAL** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
مصالحة Q/V/stock ledger/valuation ledger/GL بعد كل حركة وتحت التزامن.

## Hypothesis
قفل valuation pool يمنع lost update ويثبت basis sequence؛ append-only corrections تحافظ على التاريخ.

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
Opening10/100→Receipt10/140→Issue5/60→Receipt5/90→Return2/24→ValueAdjustment+6→Issue22/300. ثم 20 receipt/issue متزامنة.

## Steps
نفذ كل خطوة داخل ACID مع stock/value ledgers وGL/outbox؛ احسب Q/V من facts؛ اختبر آخر issue وlate price 100→110 بعد بيع النصف.

## Expected Result
كل خطوة تتصالح؛ Q=0 يعني V=0؛ delta10 ينقسم remaining5/consumed5 دون تعديل issue الأصلي -50.

## Actual Result
نجح التسلسل والتزامن؛ التصحيح الفردي انتهى remaining5/value55 وCOGS55، وبقي old issue=-50. [تفاصيل الخطوات](../evidence/mwa-steps.json).

| Test | Result | ms |
|---|---|---:|
| `mwa-sequence-ledger-value-gl-reconciliation` | PASS | 65.17 |
| `mwa-concurrent-receipts-issues-and-retries` | PASS | 113.17 |
| `late-cost-append-only-onhand-and-consumed-split` | PASS | 22.42 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
لم تُثبت bounded shadow-delta dependency graph متعددة المصادر/المراحل ولا mixed receipts مع late actual costs بعد انتقال WIP عبر أوامر متعددة. التسوية الحالية single-source فقط. Return basis ثابت من linked original، لا اختبار شامل returns across closed periods.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
Base MWA pattern صالح للتأسيس. **Late cost/multi-stage propagation implementation blocked** حتى تجربة graph/cutoff/staleness وحدود الأداء ومراجعة مالية؛ لا fallback PPV صامت.
