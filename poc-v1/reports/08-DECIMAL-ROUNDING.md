# Decimal Precision / Rounding Report

**Result: PASS** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
ضبط precision/rounding بين C# وPostgreSQL واكتشاف overflow/intermediate rounding.

## Hypothesis
BigInteger rational intermediates مع quantization واحدة ToEven تعطي PostgreSQL نفس النتيجة عند استعمال السياسة الصريحة.

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
17 متجه/مقارنة: money tiny/ties/negative، quantity، JOD3/USD2/JPY0، FX، tax/discount توضيحيان، UOM thirds، allocation residual، production وlarge values.

## Steps
قارن Multiply/Divide exact مع numeric + poc.round_even. أثبت اختلاف round الافتراضي؛ اختبر 10000 rounding operations، final residual، type overflow، UUIDv7/DateOnly/UTC roundtrip.

## Expected Result
zero uncontrolled mismatches؛ out-of-range fails؛ last allocation absorbs explicit residual.

## Actual Result
صفر فروق تحت السياسة الصريحة؛ PostgreSQL round(2.5)=3 وC# ToEven=2. intermediate multiplication تجاوز decimal؛ rational retained 48-digit numerator. numeric(24,6) رفض overflow 22003.

| Test | Result | ms |
|---|---|---:|
| `decimal-postgresql-even-rounding-vectors` | PASS | 38.14 |
| `decimal-intermediate-overflow-range-and-time-uuid` | PASS | 10.46 |

[كل القيم الفعلية](../evidence/decimal-vectors.json).

## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
NUMERIC typmod يمكنه تقريب excess scale تلقائيًا؛ API/domain يجب أن يرفضا/يطبقا quantization صراحة قبل التخزين. لا تعتمد على cast إلى numeric لتحديد القانون. tax/FX examples ليست متطلبات الأردن.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
Money24,6 / cost24,10 / qty24,8 / rate28,12 final bounds؛ ToEven سياسة تقنية داخلية مرشحة، مع versioned legal override بعد تحقق. لا تقريب متكرر للـaverage؛ issue يquantize عند ledger line؛ issue الأخير يأخذ residual.
