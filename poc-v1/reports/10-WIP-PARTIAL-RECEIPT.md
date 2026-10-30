# WIP / Partial Production Cost Report

**Result: CONDITIONAL** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
مقارنة سياسات partial output قبل اعتماد أي سياسة مالية نهائية.

## Hypothesis
كل سياسة يمكن تفسير WIP/cost/GL/history لها، لكن التقدير قد يمنح WIP credit أو يفرض تأجيل receipt.

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
Order100؛ costs500 ثم300 ثم200؛ receipts40/30/30؛ estimate12؛ actual unit10. بيع20 بعد receipt الأول لاختبار تصحيح المستهلك.

## Steps
نفذ estimated-credit-allowed وincurred-cap وwait-for-funded-estimate؛ احفظ facts الأصلية؛ أضف final inventory/COGS corrections وسوِّ WIP إلى صفر عند اكتمال السياسة.

## Expected Result
الفرق يذهب للمخزون/COGS بأثر جديد، لا rewrite؛ سياسة لا تكفيها WIP تمنع الجدول بدل إخفاء credit.

## Actual Result
Estimated: outputs480/360/360، WIP20 ثم-40 ثم-200، final variance-200، Inventory-160/COGS-40، WIP0. Cap:480/320/200، WIP20/0/0؛ final per-receipt normalizations -80/-20/+100، aggregate Inventory+40/COGS-40؛ WIP0. Funded estimate رفض receipt الثاني (320<360)، فلا يحقق الجدول المطلوب دون إعادة تخطيط.

| Test | Result | ms |
|---|---|---:|
| `wip-partial-100-40-30-30-estimated-credit-allowed` | PASS | 51.41 |
| `wip-partial-100-40-30-30-incurred-cap` | PASS | 36.96 |
| `wip-partial-100-40-30-30-wait-for-funded-estimate` | PASS | 26.13 |

[estimated evidence](../evidence/estimated-credit-allowed.json)، [incurred cap](../evidence/incurred-cap.json).

## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
نجاح المقارنة لا يعتمد temporary WIP credit. Actual rate/labor/overhead/scrap/cost dependency graph ليست مثبتة. تخزين التصحيح aggregate مع plan evidence في JSON تجربة، وليس production traceability schema مكتملًا.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
**Manufacturing cost policy remains BLOCKED for finance approval**. اعرض اختيار credit-controlled estimate مقابل cap/deferral، وحدود القيمة/المدة/الاعتماد. لا تحول Suggested في Blueprint إلى Approved.
