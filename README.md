# ERP — Foundation + Finance V1

أساس Backend/Web مع شرائح Finance المعتمدة: القيد اليدوي، Trial Balance، General Ledger Detail، والعكس اليدوي لقيد مرحّل.
العكس ينشئ قيدًا مرحّلًا مستقلًا ومتوازنًا بعلاقة غير قابلة للتغيير، ويترك القيد الأصلي كما هو.
المجلد `poc-v1/` يحتفظ بالتجارب السابقة ولا يدخل في Solution أو التطبيق.
عقود الشريحة وتشغيلها في [دليل القيد اليدوي](docs/MANUAL-JOURNAL-V1.md)، والنتائج في [تقرير قبول الشريحة](docs/evidence/journal-v1/ACCEPTANCE.md).
أدلة الأساس السابقة محفوظة في [Foundation](docs/evidence/FOUNDATION-V1.md).

## تشغيل محلي

المتطلبات: Linux/macOS shell، .NET SDK **10.0.402**، Node **24.19.0** وnpm **11.9.0**، Python3.12+، Docker Engine/Compose.
تثبيت SDK المفقود التلقائي يدعم Linux x64 ويحتفظ بفحص SHA512. PostgreSQL18.2 مثبت ببصمة لاستخدام development/test فقط.

```bash
bash scripts/bootstrap.sh
source scripts/env.sh
dotnet build Erp.slnx --no-restore
python3 scripts/local.py
python3 scripts/local.py finance-demo
python3 scripts/local.py api
```

في terminal ثانٍ شغّل `python3 scripts/local.py worker` بعد `source scripts/env.sh`، وفي ثالث:

```bash
cd src/Clients/Web
npm start
```

API على loopback5080 وWeb على4200. افحص `/health/live` و`/health/ready` بطلب محلي.
سكربت local يولد credentials/token عشوائية في `.local/settings.json` بصلاحية0600، ولا يطبعها.
افتح `/workspace` في Web وأدخل Tenant/Company التجريبيين ورمز التطوير المحلي في حقل الجلسة؛ يبقى في الذاكرة فقط.
الـWeb يتحقق من الجلسة والصلاحيات عبر الخادم؛ المصادقة الإنتاجية معلقة. يتيح [دليل التشغيل](docs/LOCAL-DEVELOPMENT.md)
اختبار adapter المحلي والـcontext دون وضع secrets في command arguments.

## التحقق

```bash
source scripts/env.sh
# Local Chromium only; CI installs the browser revision paired with Playwright.
export POC_CHROMIUM=/usr/bin/chromium
bash scripts/verify.sh
```

تشمل البوابات Release build وformatting، Unit/Architecture/PostgreSQL integration tests، اختبارات Angular والمتصفح،
فحص الاعتمادات، وpublish محلي لـartifacts فقط. لا deployment تلقائي. لا تشغّل format وbuild بالتوازي على نفس checkout.

| دليل | المحتوى |
|---|---|
| [Architecture](docs/ARCHITECTURE.md) | الحدود والتبعيات وملكية schemas |
| [Local development](docs/LOCAL-DEVELOPMENT.md) | المتطلبات وتشغيل الخدمات وإعادة التشغيل |
| [Configuration](docs/CONFIGURATION.md) | secrets، الهوية التجريبية، defaults |
| [Migrations](docs/MIGRATIONS.md) | المالك المنفصل وN→N+1 |
| [Testing](docs/TESTING.md) | الاختبارات والتزامن والأعطال وCI |
| [Security](docs/SECURITY.md) | حدود RLS والتفويض والخصوصية |
| [Compatibility](docs/COMPATIBILITY.md) | النسخ والتراخيص والترقية المقصودة |
| [ADRs](docs/adr/FOUNDATION-V1.md) | قرارات التنفيذ والانحرافات المعلنة |
| [Blockers](docs/BLOCKERS.md) | ما لم يؤذن بتنفيذه |

أُجيزت شرائح Manual Journal وTrial Balance وGeneral Ledger Detail وManual Journal Reversal وFiscal Period Close وFiscal Period Setup وChart of Accounts Setup وAccounting Book Setup وManual Journal Correction. أي شريحة أخرى تتطلب اعتمادًا جديدًا.
التقرير موضح في [Trial Balance](docs/TRIAL-BALANCE-V1.md) و[تقرير قبوله](docs/evidence/trial-balance-v1/ACCEPTANCE.md).
لا يُعاد استخدام Development adapter أو كلمات المرور المحلية في إنتاج.

General Ledger Detail V1 is implemented within Finance: [contract and development guide](docs/GENERAL-LEDGER-V1.md), [acceptance evidence](docs/evidence/general-ledger-v1/ACCEPTANCE.md). Trial Balance account links drill into paginated posted movements and the existing immutable journal/audit screen.

Manual Journal Reversal V1 is implemented within Finance: [contract and development guide](docs/MANUAL-JOURNAL-REVERSAL-V1.md), [ADR](docs/adr/MANUAL-JOURNAL-REVERSAL-V1.md), and [acceptance evidence](docs/evidence/reversal-v1/ACCEPTANCE.md).

Fiscal Period Close V1 is implemented within Finance: [contract and development guide](docs/FISCAL-PERIOD-CLOSE-V1.md), [ADR](docs/adr/FISCAL-PERIOD-CLOSE-V1.md), and [acceptance evidence](docs/evidence/period-close-v1/ACCEPTANCE.md).

Fiscal Period Setup V1 is implemented within Finance: [contract and development guide](docs/FISCAL-PERIOD-SETUP-V1.md), [ADR](docs/adr/FISCAL-PERIOD-SETUP-V1.md), and [acceptance evidence](docs/evidence/period-setup-v1/ACCEPTANCE.md). The local demo now creates its period through the Web screen.

Chart of Accounts Setup V1 is implemented within Finance: [contract and development guide](docs/CHART-OF-ACCOUNTS-SETUP-V1.md), [ADR](docs/adr/CHART-OF-ACCOUNTS-SETUP-V1.md), and [acceptance evidence](docs/evidence/account-setup-v1/ACCEPTANCE.md). The local demo now creates accounts through the Web screen.

Accounting Book Setup V1 is implemented within Finance: [guide](docs/ACCOUNTING-BOOK-SETUP-V1.md), [ADR](docs/adr/ACCOUNTING-BOOK-SETUP-V1.md), and [acceptance evidence](docs/evidence/book-setup-v1/ACCEPTANCE.md). New local books, periods and accounts are created through their Web screens.

Manual Journal Correction V1 is implemented within Finance: [guide](docs/MANUAL-JOURNAL-CORRECTION-V1.md), [ADR](docs/adr/MANUAL-JOURNAL-CORRECTION-V1.md), and [evidence](docs/evidence/correction-v1/ACCEPTANCE.md).
