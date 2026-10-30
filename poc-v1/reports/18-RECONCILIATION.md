# Reconciliation Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
اكتشاف كل اختلاف مالي/مخزني في النماذج المنفذة بدون إصلاح صامت.

## Hypothesis
مصادر facts append-only تُطابق projections والمجاميع بعد كل حركة وبعد restore.

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
كل pools بما فيها افتتاحيات reservation، MWA، late cost، FG policies، partial allocation fixture، journals.

## Steps
ReconcilePool بعد كل حركة/سباق؛ SUM(stock qty/value)، separate valuation ledger وInventory GL؛ GLdebit-credit؛ allocation bound؛ WIPfacts/GL عند close؛ fingerprints بعد restore.

## Expected Result
zero unexplained difference؛ WIP policy رفضت partial schedule تبقى مفتوحة مع WIP160 معلن، لا close وهمي.

## Actual Result
Restore scan: stock mismatches=0، valuation mismatches=0، global GL difference=0.000000. Estimated/Cap closed WIP=0؛ partial allocation original100=allocated100+remaining0.

| Test | Result | ms |
|---|---|---:|
| `open-item-partial-allocation-reconciliation` | PASS | 5.90 |
| `global-ledger-openitem-reconciliation` | PASS | 3.34 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
AR fixture أحادي debit item مع partial amounts؛ لا يمثل AP أو payment endpoints متعددة العملات أو control-account AR/AP reconciliation كاملًا. FX numeric vector ليس settlement flow. ستحتاج full typed debit-credit allocation vectors قبل AR/AP production features. Complete downstream costing graph pending.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
احتفظ بالمصالحة كfailure واضح. اختلاف earlier reservation setup كان fixture imbalance، عُولج بإضافة **opening ledger entries** ثم أعيد تشغيل كل السباقات/restoration، ولم تُعدّل facts المنشورة في المكان.
