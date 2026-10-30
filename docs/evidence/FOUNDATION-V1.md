# Implementation Foundation — V1

**Gate: FOUNDATION PASS — للنطاق المأذون Backend/Web Foundation فقط.**

التاريخ:18 أكتوبر2026 (Asia/Amman). لا Finance/Inventory/Sales feature تنفيذية، ولا نشر إنتاج أو موارد cloud مدفوعة.
الحالة معروضة لاعتماد الانتقال؛ لا إذن تلقائي لمرحلة لاحقة.

## Implemented

38 مشروع .NET10 في Solution واحدة، وحدود واضحة لـPlatform وسبع وحدات أعمال وAPI وWorker وMigration host وTests.
المتاح في وحدات الأعمال Skeletons/Contracts boundaries/Registration فقط. Platform تنفذ:
verified tenant/company context، transaction-local RLS، explicit shared-UoW/EF enlistment، concurrency tokens،
Idempotency Processing/Completed/fingerprint/receipt/conflict، Outbox leasing/retry/backoff/Failed وInbox deduplication،
append-only Audit مع mask-all snapshots، Stable Error registry، request/correlation/trace، OTel وhealth/config/secrets foundations.
Angular Shell يحوي routing/lazy boundaries وArabic/English وRTL/LTR وtheme tokens وAPI/error/auth/authz abstractions.

## Tests

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| .NET Unit |6|0|0|
| Architecture |9|0|0|
| PostgreSQL integration / HTTP / migrations / recovery |23|0|0|
| Angular TestBed / Vitest4.1.12 |5|0|0|
| Playwright browser checks |8|0|0|
| **Total automated checks** |**51**|**0**|**0**|

إضافةً إلى ذلك اجتاز تشغيل API/Worker الحقيقي ودليل الإعداد المحلي smoke test مستقلًا.
Release build: **0 warnings /0 errors**؛ Angular production build نجح؛ formatting/type checks وlocked restores وdependency scans نجحت.
نُشرت artifacts محليًا فقط لـAPI/Worker. لا remote GitHub Actions run بدأ؛ workflow جاهز للـreviewed push.

الأدلة: [summary.json](summary.json)، [Unit TRX](Erp.UnitTests.trx)، [Architecture TRX](Erp.ArchitectureTests.trx)،
[Integration TRX](Erp.IntegrationTests.trx)، [Angular JSON](angular-unit-results.json)، [Browser JSON](browser-results.json)،
[Startup smoke](startup-results.json)، [Final verify log](verify-final.log)، [Bootstrap log](bootstrap-final.log)،
[NuGet scan](nuget-audit.json)، [npm scan](npm-audit.json)، [installed licenses](dependency-licenses.csv).

## Architecture

الحدود سليمة في project graph وcompiled assembly references. Domain→Infrastructure وbusiness cross-module Infrastructure
وSales→Finance/Inventory persistence وWeb/host DB access تُرفض. injected forbidden edges تثبت أن القواعد تكتشف المخالفة.
Platform shared plumbing هو الاستثناء الموثق للوحدات Infrastructure؛ لا مشاركة business persistence. انظر [القواعد](../ARCHITECTURE.md).

## Security

Tenant A لا يقرأ/يكتب Tenant B؛ Company C1 لا يصل C2. missing context يفشل مغلقًا. physical PID يثبت إعادة استخدام
الاتصال A→B دون سياق متبقٍ؛ rollback/cancellation/worker paths مغطاة. هوية owner/superuser/BYPASSRLS لا تجتاز guard.
HTTP الحقيقي:200 للمسموح،401 دون token،403 لtenant/company غير المصرح؛ Dev identity ممنوعة خارج Development/Test.
راجع [حدود الحماية](../SECURITY.md): RLS ليست authentication أو حماية ضد arbitrary SQL/process compromise.

فحص npm الأول رفض Vitest4.0.18 بسبب GHSA-5xrq-8626-4rwp وGHSA-82fw-gwwq-j7x9؛ رُقي عمدًا إلى4.1.12
ضمن Angular peer range، ثم نجحت الاختبارات وscan بصفر findings. [الدليل السابق](history/npm-audit-before-vitest-fix.json)
محفوظ؛ لا suppressions أو ranges مُرخاة. ملفات التراخيص تثبت xUnit Apache2 بدل افتراض MIT.

## Database

Runtime migrator منفصل؛ API/Worker لا ينفذان DDL عند البدء. أربع foundation tables فقط خارج history metadata؛
`foundation_test` يخص حاويات الاختبار ولا يظهر في dev/runtime migrations.
المعاملات أثبتت commit ذريًا وrollback بعد SaveChanges، missing required effect rejection، permanent source uniqueness،
bounded full-UoW SQLSTATE retry، stale EF token، replay/concurrent duplicates/timeout after commit/response loss.
Outbox بقي بعد قتل عمليتي worker فعليًا قبل send وبعد consumer commit؛ التسليم يتكرر والأثر واحد عبر Inbox؛ stale ACK مرفوض،
backoff/failed/exhausted lease مغطاة. لا exactly-once claim. Audit immutable وreceipt لا يثبت Processing غير مكتمل.
N clean install وN→N+1 upgrade/reapply وdata survival وno pending model changes نجحت.

## Web

Angular22.2.3 +TypeScript6.0.3 +Node24.19.0؛ لم يستخدمTS7.0.2. Arabic/English وRTL/LTR وlazy navigation وlanguage persistence
وpermission directive وsafe API error handling نجحت. Playwright استعمل Chromium154 المحلي بصراحة؛ CI يثبت managed revision
المقترنة بحزمته. لا production login أو ERP screens أو Mobile implementation. [لقطة RTL](shell-rtl.png).

## CI

[Workflow](../../.github/workflows/foundation.yml) بSHA-pinned actions وcontents:read. يفرض frozen restore/npm ci، exact pins/EF alignment،
Release build، formatter/type checks، Unit/Architecture/PostgreSQL/Web/browser tests، moderate+dependency gates وartifact generation.
نفس entry point [verify.sh](../../scripts/verify.sh) نُفذ محليًا واكتمل. النشر الإنتاجي غير موجود.

إعداد البيئة: حُدثت مسودة `install_script` و`start_skill` إلى Foundation مع حفظ الشبكة ومتطلبات secrets كما كانت.
[إيصال الحفظ](environment-draft.json) يثبت حفظ المسودة فقط؛ لم يُنشر snapshot أو يُختبر الاسترجاع في مهمة جديدة.
لتفعيلها راجع واحفظ التغييرات في إعدادات البيئة ثم انشر البيئة. ملفات الكود محلية وغير committed حتى الآن.

## Foundation acceptance mapping

| # | Required proof | Evidence |
|---|---|---|
|1|Solution builds cleanly|Final verify:0warnings/0errors|
|2|Approved Angular compatibility tuple|Compatibility pins + build + package locks|
|3|Forbidden dependency fails tests|Architecture negative-edge tests + compiled refs|
|4|Tenant A cannot access B|SQL RLS and HTTP denied cases|
|5|Company scope enforced|Company mismatch SQL/HTTP + worker scope test|
|6|No pooled context leak|Same physical PID A→B, rollback/cancel regression|
|7|Rollback no partial state|Two contexts + audit/outbox/receipt atomicity tests|
|8|No duplicate idempotent effect|8 concurrent contenders + source uniqueness + replay|
|9|Outbox survives worker restart|Physical kill harness + expired-lease recovery|
|10|Inbox handles duplicate|Concurrent consumers + hash conflict + atomic rollback|
|11|Correlation request→DB→outbox→worker|HTTP test asserts matching correlation/trace in DB and captured Npgsql/worker spans|
|12|Clean migrations work|Fresh container + separate migration identity|
|13|Previous→current migration|N seeded row retained after N+1, repeat migration/no pending model changes|
|14|CI executes critical tests|Workflow invokes tested verify.sh; local complete run; remote run pending reviewed push|
|15|Arabic/English+RTL/LTR Shell|5 unit +8 browser checks|

## Blockers and deviations

[Known blockers](../BLOCKERS.md) تستمر كما أُقرّت: WIP/Manufacturing، complex late cost، full AR/AP/FX، complex reservations،
production OIDC/MFA، Flutter، JoFotara، cloud deployment/PITR والامتثال. لا تتحول إلى أعمال منفذة عبر skeleton أو placeholder policy.

انحرافات موثقة: skeleton assemblies أُنشئت مقدمًا بناءً على الطلب الأحدث؛ Platform supporting adapters مشتركة؛ uppercase registry
IDs aliases لـdotted wire codes؛ Quartz لا يضاف دون scheduled use case، والعامل الحالي polling فقط؛ لا destination وهمية لـOTel exporter.
PoC محفوظ ومعزول عن Solution. قُيِّد MSBuild parallelism وتوقف تشغيل format/build المتزامن بعد فشل infrastructure عابر؛ final gate مرّ كاملًا.
راجع [ADRs](../adr/FOUNDATION-V1.md).

## Next Recommended Vertical Slice

**Manual Journal Entry — Draft → Validate → Post**، شركة ودفتر واحدان وفترة مفتوحة وحسابات دنيا، مع Balance/immutable GL/Audit/Outbox/Idempotency.
هذا اقتراح واحد فقط؛ لا AR/AP/FX أو مخزون/تصنيع/JoFotara أو أثر خارجي ضمنه. **لم يبدأ ويحتاج اعتمادًا جديدًا.**

**Gate closed for automatic continuation: FOUNDATION PASS. Await user approval.**
