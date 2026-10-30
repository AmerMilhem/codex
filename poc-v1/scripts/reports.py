#!/usr/bin/env python3
"""Render the sprint evidence pack from recorded runs; never converts pending proof into PASS."""
import collections,csv,json,pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1];E=ROOT/'evidence';R=ROOT/'reports';R.mkdir(exist_ok=True)
run=json.loads((E/'results.json').read_text());tests=run['results'];browser=json.loads((E/'angular-browser.json').read_text());backup=json.loads((E/'backup-restore.json').read_text());licenses=json.loads((E/'license-review.json').read_text())
def write(name,text): (R/name).write_text(text.strip()+'\n')
def detail(name):return next(t['detail'] for t in tests if t['name']==name)
def rows(category):return [t for t in tests if t['category'] in category.split(',')]
def table(items):return '| Test | Result | ms |\n|---|---|---:|\n'+'\n'.join(f"| `{t['name']}` | {t['status']} | {t['milliseconds']:.2f} |" for t in items)
def report(file,title,status,category,objective,hypothesis,data,steps,expected,actual,limits,recommendation,extra=''):
 items=rows(category) if category else []
 if any(t['status']=='FAIL' for t in items):status='FAIL'
 write(file,f'''# {title}

**Result: {status}** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: {run['utc']}.

## Objective
{objective}

## Hypothesis
{hypothesis}

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
{data}

## Steps
{steps}

## Expected Result
{expected}

## Actual Result
{actual}

{table(items) if items else ''}

{extra}

## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
{limits}
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
{recommendation}
''')
sources={x['name']:x for x in json.loads((E/'official-source-manifest.json').read_text())['sources']}
matrix=[
('.NET','SDK 10.0.402; runtime 10.0.13','net10.0; fixed global.json','LTS 10; policy evidence, next servicing patch separately reviewed','MIT','SDK SHA512 verified; Linux x64 only','PASS','dotnet-policy'),
('ASP.NET Core','10.0.13','shared framework + OIDC package 10.0.13','follows .NET 10 LTS','MIT','Kestrel/OIDC loopback; managed TLS/IdP not proven','PASS','dotnet-policy'),
('EF Core','10.0.13','net10.0; all resolved EF components aligned','10.x LTS alongside .NET; keep patch alignment','MIT','3 contexts explicitly enlisted; no implicit independent commit','PASS',None),
('Npgsql EF provider','10.0.3','EF Core/Relational >=10.0.4 <11.0.0; Npgsql >=10.0.3','upstream stable package; no invented vendor SLA','PostgreSQL','observed 10.0.13 tuple; not every version in range tested','PASS',None),
('Npgsql','10.0.3','net10.0 target tested','stable package; patch policy required','PostgreSQL','pool tested directly; PgBouncer not tested','PASS',None),
('PostgreSQL','18.2 + OCI digest pinned','18.x selected; exact executed 18.2','18 major supported; freshest servicing patch availability unresolved','PostgreSQL','18.5 image unavailable; 18.2 is PoC pin, not approved production patch','PASS','postgres-tag'),
('Angular core/forms/compiler/CLI/build','22.2.3','Node ^22.22.3 OR ^24.15.0 OR >=26.0.0; TS >=6.0 <6.1','22 active major in official support table; no perpetual patch support claim','MIT','zoneless typed forms; no commercial grid adopted','PASS','angular-compiler'),
('TypeScript','6.0.3','>=6.0 <6.1 for this Angular compiler','fixed compiler; no LTS promise','Apache-2.0','7.0.2 explicitly excluded','PASS','typescript'),
('Node.js','24.19.0','satisfies ^24.15.0; Playwright >=20','24 LTS; schedule EOL 2028-04-30','MIT plus bundled notices','actual preinstalled version, not previous 24.22 candidate','PASS','node-schedule'),
('Flutter','UNSELECTED','bundled stable Flutter/Dart pair must be confirmed','PENDING exact supported stable release','BSD-3-Clause source only','official archive CONNECT 403; SDK not installed; no mobile PoC started','PENDING','flutter-release-archive'),
('Dart','UNSELECTED','must be bundled with selected Flutter, not pubspec range alone','PENDING paired release','BSD-3-Clause source only','neither stable branch constraint nor independent Dart latest proves pair','PENDING','dart-license'),
('Quartz.NET','4.5.1','net10.0 tested','stable 4.x candidate; no stated LTS/SLA','Apache-2.0','Quartz 4 API differs: QuartzSchedulerBuilder; Execute includes CancellationToken; RAM store only','PASS',None),
('OpenTelemetry','1.20.0 SDK/Hosting/AspNetCore instrumentation','resolved graph on net10.0','stable 1.x packages; instrumentation policy varies','Apache-2.0','1 local exported span; OTLP collector/hosted rollout pending','PASS',None),
('Testcontainers.PostgreSql','4.16.0','net10.0 + Docker 28.4.0 / API 1.51','stable test-only dependency','MIT','real disposable PG container functional probe; no production dependency','PASS','testcontainers-license'),
('Playwright Node','1.64.1','Node >=20; managed browser normally tied to package','stable test tool, pinned','Apache-2.0','tested installed Chromium 154.0.8037.57 via executablePath, not bundled revision; other browsers pending','PASS','playwright'),
]
out='''# Verified Compatibility Matrix — V1

**Scope:** Selected and executed PoC tuple, not a deployment/security certification. PASS means the listed compatibility
was demonstrated under the stated constraint; unsupported scenarios remain explicit. No `latest` issuance policy.
Sources were fetched again during this sprint; hashes/status are in [official-source-manifest.json](../evidence/official-source-manifest.json).
Package restore and checksums stayed enabled. [SDK SHA512](../evidence/sdk-checksum.json).

| Component | Selected version | Compatible range | Support status | License | Known constraint | Result | Official evidence |
|---|---|---|---|---|---|---|---|
'''
nuget_urls={'EF Core':'microsoft.entityframeworkcore/10.0.13/microsoft.entityframeworkcore','Npgsql EF provider':'npgsql.entityframeworkcore.postgresql/10.0.3/npgsql.entityframeworkcore.postgresql','Npgsql':'npgsql/10.0.3/npgsql','Quartz.NET':'quartz/4.5.1/quartz','OpenTelemetry':'opentelemetry/1.20.0/opentelemetry'}
for component,version,compat,support,license,constraint,status,source in matrix:
 url=sources[source]['url'] if source else 'https://api.nuget.org/v3-flatcontainer/'+nuget_urls[component]+'.nuspec'
 out+='| '+' | '.join([component,version,compat,support,license,constraint,status,f'[official]({url})'])+' |\n'
out+='''
Angular peer ranges are recorded verbatim from exact `compiler-cli@22.2.3`, not inferred from a tag or a major-version table.
Node 24.19.0 and TypeScript 6.0.3 were actually used for the successful build. RxJS 7.8.2 fits the declared peer range;
zone.js is optional and omitted under the tested zoneless configuration. 11 browser checks passed.

PostgreSQL image: `postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2`.
The attempted 18.5 tag returned manifest unknown. Official moving branch currently mentioning a later patch is not proof
that a distributable image was available. Re-pin and rerun against the vendor's supported current patch before a real deployment.
Flutter/Dart remain PENDING, not FAIL and not PASS. This blocks mobile dependency adoption; mobile launch criticality remains OPEN.
'''
write('01-COMPATIBILITY.md',out)
license_rows=list(csv.DictReader((E/'resolved-license-matrix.csv').open()))
counts=collections.Counter(x['license'] for x in license_rows)
write('02-LICENSES.md',f'''# License Matrix — V1

**Result: PASS for identifying licenses in the executed Linux dependency graph; distribution obligations remain CONDITIONAL.**

[Full installed dependency matrix](../evidence/resolved-license-matrix.csv): {licenses['resolvedPackages']} packages,
{licenses['nuget']} NuGet and {licenses['npm']} npm. Missing/file-only expressions requiring identification: {len(licenses['manualFileOrMissingReview'])}.
Direct components and official license links appear in [Compatibility](01-COMPATIBILITY.md). Flutter/Dart source licenses
do not clear unselected mobile plugins. Managed OIDC, cloud service terms, fonts, scanners and notification plugins remain unselected.

| Declared expression | Installed packages |
|---|---:|
'''+ '\n'.join(f'| `{k}` | {v} |' for k,v in sorted(counts.items()))+'''

Critical constraints: Angular is MIT; TypeScript, Quartz, OTel and Playwright Apache-2.0; EF/.NET MIT;
Npgsql and PostgreSQL PostgreSQL License; Testcontainers MIT. No paid UI/grid component was introduced.
`lightningcss` and native build packages are **MPL-2.0**, `caniuse-lite` is **CC-BY-4.0**;
`@bufbuild/protobuf` declares **Apache-2.0 AND BSD-3-Clause**. Preserve notices and examine any redistributed
tooling/native artifacts and modifications; do not describe the full build graph as MIT-only.
The HTML grid ships Angular/application assets, not the native CSS build tool by default.

License expressions come from restored nuspec/package metadata; this is not a full binary SBOM or legal opinion.
Cross-platform optional packages not installed on this Linux machine are outside this installed-graph count.
`packages.lock.json` and `package-lock.json` pin resolution and integrity. `npm audit` reports zero findings
at the recorded time; NuGet audit returned no listed vulnerable packages. Neither scan guarantees absence of defects.
''')
report('03-EF-TRANSACTIONS.md','EF/Npgsql Transaction Report','PASS','transactions',
 'إثبات وحدة المعاملة المحلية عبر Business + Finance + Integration contexts.',
 'تستطيع ثلاثة DbContexts المشاركة في اتصال ومعاملة واحدة؛ SaveChanges لا يثبت جزءًا مستقلًا.',
 'Invoice 123.456789، AR item، journal/lines، audit، outbox؛ مصدر فريد، فترة مفتوحة ومغلقة.',
 'ابدأ NpgsqlTransaction؛ اضبط scope محليًا؛ UseTransaction لكل context؛ احفظ بالتتابع ثم commit. كرر rollback، unique failure، terminate backend وdeadlock.',
 'إما جميع الآثار أو لا شيء؛ 23505/40P01/connection interruption لا تترك نصف عملية.',
 'نجحت المشاركة الفعلية، rollback بعد SaveChanges، وفشل unique، وقطع الاتصال. كشف PostgreSQL ضحية deadlock واحدة؛ نجحت معاملة جديدة بترتيب أقفال ثابت.',
 'EF automatic execution strategy retry غير مفعّل. تجربة retry أعادت UoW صراحة؛ unknown commit يُحسم عبر receipt. لا توزيع معاملات ولا سياقات متوازية على نفس الاتصال.',
 'اعتماد هذا النمط كأساس تجريبي؛ يجب تكرار الاختبار بعد أي تغيير provider، pooling/proxy أو transaction orchestration.')
report('04-RLS-POOLING.md','Tenant RLS / Pooling Report','PASS','security',
 'عزل tenant/company دون الاعتماد على query filter، بما يشمل reuse/cancel/rollback والعمال.',
 'RLS مع verified transaction-local context وهوية غير مالكة يفشل مغلقًا ولا يحتفظ بسياق A للطلب B.',
 'Tenant A/C1، A/C2، B/C3؛ pool مستقل MaxPoolSize=1 لإثبات نفس backend PID.',
 'اقرأ A؛ أعد الاتصال للـpool؛ افتح B على PID نفسه؛ اقرأ دون context؛ جرّب write خارج النطاق وresource ID معدّل؛ ألغِ pg_sleep وأعد التنفيذ كعامل.',
 'لا صفوف أو writes خارج scope؛ missing context صفر/رفض؛ لا دعم إداري ضمني.',
 json.dumps(detail('rls-pool-reuse-rollback-cancel-worker-support'),ensure_ascii=False),
 'نتيجة RLS تقتصر على تطبيق موثوق يختار scope بعد التفويض. custom GUC قابلة للتغيير من SQL؛ ليست اعتمادًا لهوية العميل ولا حماية ضد SQL injection/اختراق العملية. PgBouncer، prepared-query matrix وsupport approval workflow الكامل غير منفذة.',
 'يمكن بدء persistence foundation بهذا الضبط، مع رفض serving traffic إذا كانت الهوية owner/BYPASSRLS. احتفظ باختبار الـPID في CI.')
res=rows('reservation')[:4]
comparison='| Strategy | Races | Double success | Retries | Deadlocks | Winner p95 ms |\n|---|---:|---:|---:|---:|---:|\n'+'\n'.join(f"| {x['name'].split('8-vs-7-')[1]} | {x['detail']['rounds']} | {x['detail']['doubleWinners']} | {x['detail']['retries']} | {x['detail']['deadlocks']} | {x['detail']['p95WinnerMs']:.3f} |" for x in res)
report('05-INVENTORY-CONCURRENCY.md','Inventory Reservation Concurrency Report','PASS','reservation',
 'مقارنة صحة سباق الحجز مع استراتيجيات PostgreSQL متعددة.',
 'لا يمكن حجز 8 و7 معًا من Available=10؛ reservation events وreserved projection يلتزمان بالمعاملة نفسها.',
 '25 سباقًا لكل استراتيجية، 100 سباق إجمالًا؛ لكل منتج opening ledger 10/value100 وGL، لا balance seed بلا ledger.',
 'حرر مهمتين معًا، احجز بالـatomic update/row lock/Serializable/optimistic revision. أعد UoW حتى 3 مرات للأخطاء القابلة للإعادة، وارفض عدم الكفاية.',
 'فائز واحد في كل سباق؛ reserved=7 أو8، never15؛ projection=reservation events؛ stock/value=ledgers/GL.',
 '100 سباق: صفر double winners؛ stale revision أثّر في صفر صف. راجع المقارنة المقاسة أدناه.',
 'pg_sleep/Task.Delay القصير مقصود لتوسيع نافذة السباق، لذلك الأرقام لا تعطي ranking إنتاجيًا محايدًا ولا throughput capacity. تعدد المنتجات/المخازن وrelease/expiry وإدخال bucket غائب تظل حالات لاحقة قبل reserve production feature.',
 'Atomic conditional update مناسب للحجز الأحادي البسيط؛ row locks مع ترتيب ثابت للعمليات متعددة الموارد. لا تفرض Serializable افتراضيًا على كل العمليات. صحة Ledger أهم من فرق الميلي ثانية.',comparison)
report('06-IDEMPOTENCY.md','Idempotency Report','PASS','idempotency',
 'حماية posting intent من إعادة الطلب والمهلة وفقدان الاستجابة.',
 'مفتاح ضمن tenant/company/caller/command/target وبصمة تشمل original revision يحفظ receipt واحدًا، مع source uniqueness مستقلة.',
 'Invoice 100، X، eight contenders؛ timeout caller؛ طفل يُقتل بعد commit وقبل response.',
 'أرسل مرة وتسلسليًا ومتزامنًا، وبمحتوى مختلف. اقتل العملية بعد durable marker ثم أعد نفس intent. افحص journal/outbox/document والرفض بعد سحب التفويض.',
 'Same key/same fingerprint returns same id; different fingerprint conflict; changed key لا يعيد source effect. لا invoice stock effect أصلًا؛ إصدار المخزون فعل منفصل.',
 'كل الحالات نفذت بأثر محلي واحد. اختبار فقد الاستجابة قتل عملية فعلية. unique business source منع تكرار posting بمفتاحين.',
 'Client timeout الأول هو WaitAsync في harness وليس رحلة HTTP كاملة؛ process-kill يغطي committed/no response. لا إثبات exactly-once لإرسال خارجي؛ outbox/inbox منفصلان. لم تثبت سياسة key retention أو eviction زمنية.',
 'احتفظ بuniqueness دائمة للأثر؛ لا تعتمد على TTL وحدها. أعد current authorization قبل receipt replay.')
report('07-ACCOUNTING.md','Accounting Posting Report','PASS','accounting',
 'Business operation تتطلب GL لا يمكن تثبيتها دون الآثار المحلية الإلزامية.',
 'قواعد posting ثابتة للتجربة AR→Revenue، مع period/account checks وdeferred balance/source constraints، تحقق atomic posting.',
 'فترة مفتوحة/مغلقة؛ حساب صحيح/مفقود/Inactive؛ balanced/unbalanced journals؛ concurrent duplicate source.',
 'Post document باستخدام rule v1؛ أنشئ journal/lines، AR وaudit/outbox داخل المعاملة. اختبر commit مباشر لمستند دون ledger، ثم الحالات السلبية.',
 'Debit=Credit؛ no duplicate source؛ closed/invalid/inactive rejects؛ missing ledger يمنع business commit.',
 'deferred required-effects trigger رفض المستند بلا GL/AR/Audit/Outbox؛ unbalanced journal ألغى الكل. concurrent duplicate posting نتج عنه أثر واحد.',
 'هذه قاعدتا Debit/Credit ثابتتان وتسجيل rule/event في audit، وليست rule engine إنتاجية أو tax profile أو fiscal clearance. الفترة محمية FOR SHARE؛ race الإغلاق مع reconciliation watermark غير منفذ بعد.',
 'يسمح بتأسيس engine contracts/local UoW؛ يجب اختبار profiles/dimensions/period-close barrier قبل accounting features الحقيقية.')
report('08-DECIMAL-ROUNDING.md','Decimal Precision / Rounding Report','PASS','decimal',
 'ضبط precision/rounding بين C# وPostgreSQL واكتشاف overflow/intermediate rounding.',
 'BigInteger rational intermediates مع quantization واحدة ToEven تعطي PostgreSQL نفس النتيجة عند استعمال السياسة الصريحة.',
 '17 متجه/مقارنة: money tiny/ties/negative، quantity، JOD3/USD2/JPY0، FX، tax/discount توضيحيان، UOM thirds، allocation residual، production وlarge values.',
 'قارن Multiply/Divide exact مع numeric + poc.round_even. أثبت اختلاف round الافتراضي؛ اختبر 10000 rounding operations، final residual، type overflow، UUIDv7/DateOnly/UTC roundtrip.',
 'zero uncontrolled mismatches؛ out-of-range fails؛ last allocation absorbs explicit residual.',
 'صفر فروق تحت السياسة الصريحة؛ PostgreSQL round(2.5)=3 وC# ToEven=2. intermediate multiplication تجاوز decimal؛ rational retained 48-digit numerator. numeric(24,6) رفض overflow 22003.',
 'NUMERIC typmod يمكنه تقريب excess scale تلقائيًا؛ API/domain يجب أن يرفضا/يطبقا quantization صراحة قبل التخزين. لا تعتمد على cast إلى numeric لتحديد القانون. tax/FX examples ليست متطلبات الأردن.',
 'Money24,6 / cost24,10 / qty24,8 / rate28,12 final bounds؛ ToEven سياسة تقنية داخلية مرشحة، مع versioned legal override بعد تحقق. لا تقريب متكرر للـaverage؛ issue يquantize عند ledger line؛ issue الأخير يأخذ residual.',
 '[كل القيم الفعلية](../evidence/decimal-vectors.json).')
report('09-MOVING-AVERAGE.md','Moving Weighted Average Report','CONDITIONAL','costing',
 'مصالحة Q/V/stock ledger/valuation ledger/GL بعد كل حركة وتحت التزامن.',
 'قفل valuation pool يمنع lost update ويثبت basis sequence؛ append-only corrections تحافظ على التاريخ.',
 'Opening10/100→Receipt10/140→Issue5/60→Receipt5/90→Return2/24→ValueAdjustment+6→Issue22/300. ثم 20 receipt/issue متزامنة.',
 'نفذ كل خطوة داخل ACID مع stock/value ledgers وGL/outbox؛ احسب Q/V من facts؛ اختبر آخر issue وlate price 100→110 بعد بيع النصف.',
 'كل خطوة تتصالح؛ Q=0 يعني V=0؛ delta10 ينقسم remaining5/consumed5 دون تعديل issue الأصلي -50.',
 'نجح التسلسل والتزامن؛ التصحيح الفردي انتهى remaining5/value55 وCOGS55، وبقي old issue=-50. [تفاصيل الخطوات](../evidence/mwa-steps.json).',
 'لم تُثبت bounded shadow-delta dependency graph متعددة المصادر/المراحل ولا mixed receipts مع late actual costs بعد انتقال WIP عبر أوامر متعددة. التسوية الحالية single-source فقط. Return basis ثابت من linked original، لا اختبار شامل returns across closed periods.',
 'Base MWA pattern صالح للتأسيس. **Late cost/multi-stage propagation implementation blocked** حتى تجربة graph/cutoff/staleness وحدود الأداء ومراجعة مالية؛ لا fallback PPV صامت.')
report('10-WIP-PARTIAL-RECEIPT.md','WIP / Partial Production Cost Report','CONDITIONAL','wip',
 'مقارنة سياسات partial output قبل اعتماد أي سياسة مالية نهائية.',
 'كل سياسة يمكن تفسير WIP/cost/GL/history لها، لكن التقدير قد يمنح WIP credit أو يفرض تأجيل receipt.',
 'Order100؛ costs500 ثم300 ثم200؛ receipts40/30/30؛ estimate12؛ actual unit10. بيع20 بعد receipt الأول لاختبار تصحيح المستهلك.',
 'نفذ estimated-credit-allowed وincurred-cap وwait-for-funded-estimate؛ احفظ facts الأصلية؛ أضف final inventory/COGS corrections وسوِّ WIP إلى صفر عند اكتمال السياسة.',
 'الفرق يذهب للمخزون/COGS بأثر جديد، لا rewrite؛ سياسة لا تكفيها WIP تمنع الجدول بدل إخفاء credit.',
 'Estimated: outputs480/360/360، WIP20 ثم-40 ثم-200، final variance-200، Inventory-160/COGS-40، WIP0. Cap:480/320/200، WIP20/0/0؛ final per-receipt normalizations -80/-20/+100، aggregate Inventory+40/COGS-40؛ WIP0. Funded estimate رفض receipt الثاني (320<360)، فلا يحقق الجدول المطلوب دون إعادة تخطيط.',
 'نجاح المقارنة لا يعتمد temporary WIP credit. Actual rate/labor/overhead/scrap/cost dependency graph ليست مثبتة. تخزين التصحيح aggregate مع plan evidence في JSON تجربة، وليس production traceability schema مكتملًا.',
 '**Manufacturing cost policy remains BLOCKED for finance approval**. اعرض اختيار credit-controlled estimate مقابل cap/deferral، وحدود القيمة/المدة/الاعتماد. لا تحول Suggested في Blueprint إلى Approved.',
 '[estimated evidence](../evidence/estimated-credit-allowed.json)، [incurred cap](../evidence/incurred-cap.json).')
report('11-OUTBOX-RECOVERY.md','Outbox Recovery Report','PASS','outbox,failure',
 'إثبات التعافي مع at-least-once وconsumer idempotency.',
 'lease bounded + stable event id + Inbox/effect atomic تعالج crash/duplicates؛ fence يرفض ACK القديم ولا يمنع duplicate network send.',
 'حدث مالي durable؛ عاملان؛ lease tokens؛ child worker killed before and after consumer commit؛ consumer hash collision؛ HTTP timeout stub.',
 'اقتل عمليتين فعليًا قبل dispatch وبعد consumer commit؛ سرّع lease expiry صراحة؛ أعد claim؛ نفذ duplicate consumer ثم ACK fenced. اختبر DB timeout وsend timeout.',
 'يتكرر التسليم عند الحاجة؛ consumer effect واحد؛ لا claimين لlease حية؛ old ACK zero rows؛ poison payload conflict visible.',
 'عاملان قُتلا فعليًا؛ 3claims و2deliveries و1consumer effect. نموذج HTTP أنتج sendين وأثرًا واحدًا. Inbox rollback أزال marker مع effect. DB timeout ثم fresh scoped worker نجح.',
 'consumer في crash harness معاملة DB مستقلة، وHTTP provider stub منفصل؛ لا ادعاء تنفيذ provider حقيقي. poison hash مرفوض؛ circuit breaker/backoff/dead-letter UI كامل وQuartz persistent clustering غير منفذة.',
 'اعتماد at-least-once مع idempotent consumer/stable external key فقط. لا exactly-once. retry غموض مزود غير idempotent يحتاج query/reconciliation لا blind resend.')
report('12-OIDC.md','OIDC Report','CONDITIONAL','oidc',
 'اختبار Minimal BFF auth عبر ASP.NET middleware مع تفويض ERP حالي.',
 'Authorization code+PKCE/state/nonce والتحقق من token لا يساوي ERP authorization أو membership.',
 'RSA local issuer، user-1، service-1، token يحوي tenant_hint غير مخول؛ cookie session؛ enabled/membership flags.',
 'شغّل discovery/JWKS/authorize/token/logout fixtures وBFF منفصلًا؛ اتبع login redirects؛ افحص PKCE، cookie، CSRF، expiry/audience/signature وعضوية مسحوبة ثم logout؛ جرب refresh/client credentials.',
 'token الصحيح لا يصرح بعمل ERP تلقائيًا؛ claim tenant لا يُعتمد؛ disabled/removed→403؛ bad token denied؛ logout→401.',
 json.dumps(detail('oidc-code-pkce-bff-current-membership-negative-tests'),ensure_ascii=False),
 'الـissuer بروتوكولي محلي وليس managed IdP؛ لا شاشة إدخال اعتماد/MFA حقيقية. refresh endpoint نجح، لكن automatic BFF renewal/rotation/reuse detection/provider logout/revocation/outage لم تثبت. Service account concept فقط؛ لا M2M authorization كامل. HTTP loopback وSameSite Lax/query للـfixture لا يُنقلان إلى production TLS/config.',
 'اختيار provider/test tenant ثم MFA enrollment/challenge/session rotation/revocation/issuer outage، BFF TLS/CSRF/browser cookies وmobile PKCE شرط قبل authentication production readiness. Business grants تبقى في ERP.')
report('13-ANGULAR.md','Angular Technical PoC Report','PASS','',
 'التأكد من قدرة Angular 22.2.3 على grid تقني قابل للتحرير وRTL دون شراء مكون تجاري.',
 'typed reactive FormArray مع zoneless signals وجدول HTML يمكنه معالجة100 سطر في السيناريو الأساسي.',
 '100 سطر أسماء عربية، quantities/prices؛ cost column controlled by synthetic permission؛ لا بيانات حقيقية.',
 'bash scripts/web.sh يعيد npm ci من lock ثم production build؛ خادم ملفات loopback؛ Playwright يختبر اللغة، keyboard، invalid/corrected quantity، visibility وDOM/runtime errors.',
 '100 rows؛ Arabic RTL/English LTR؛ focus التالي؛ validation يمنع submit؛ cost absent دون grant fixture.',
 f"نجح build و{len(browser['checks'])} فحصًا. initial render measured {browser['checks'][0]['detail']['renderMs']:.2f}ms، عينة محلية واحدة وليست performance SLA.",
 'لا screen-reader audit أو Safari/Firefox أو 1000-row stress. permission toggle fixture ليس security boundary؛ server DTO omission اختبر منفصلًا في OIDC. Used installed Chromium with explicit executablePath، وليس Playwright bundled revision. لا production UI أو commercial grid adoption.',
 'استخدم هذا كإثبات جدوى أولي؛ يبدأ design system بعد اعتماد Foundation، وتُدرس virtualization/pagination عند قياس مبرر؛ لا license أو grid purchase حاليًا.',
 '[Browser results](../evidence/angular-browser.json)، [screenshot](../evidence/angular-grid.png)، [source](../web/src/main.ts).')
report('14-FLUTTER.md','Flutter Compatibility / PoC Report','PENDING','',
 'إغلاق exact Flutter/Dart pair قبل أي mobile harness.',
 'لا يجوز الاستدلال من stable branch SDK constraint أو Dart latest على bundled pair.',
 'لا SDK ولا plugin dependency ولا mobile project منشأ.',
 'أُعيد طلب official releases_linux.json؛ احتفظت محاولة المصدر وخطئها في manifest. لم يبدأ Flutter PoC طبقًا للبوابة المطلوبة.',
 'لا PASS إلا مع archived release + bundled Dart + checksum ثم device checks.',
 'CONNECT403 من storage.googleapis.com؛ Flutter/Dart pair UNSELECTED؛ كل mobile runtime checks NOT EXECUTED.',
 'OIDC/secure token storage/API/barcode/QR/RTL/retry/idempotency/notifications على Android وiOS جميعها Pending. لا أجهزة فعلية أو iOS signing/SDK. BSD source license ليس plugin clearance.',
 'افتح destination في إعدادات البيئة ثم تحقق من SDK/hash/pair. افصل mobile implementation بوابةً معلقة؛ mobile launch-criticality قرار OPEN ولا يُفترض تأجيلها. لا offline posting.')
report('15-MIGRATION.md','Migration Report','PASS','migration',
 'اختبار expand→mixed-version compatibility→backfill→contract.',
 'nullable expansion + dual-compatible writer + bounded backfill تسمح بتعايش N وN+1 قبل retirement المنضبط.',
 '3 صفوف؛ old_name→new_name؛ كتابة N أثناء backfill وكتابة N+1.',
 'أضف column، trigger يحافظ على التوافق في الاتجاهين، اقرأ COALESCE؛ backfill صفحة1؛ تحقق بلا mismatches؛ validate NOT NULL؛ أوقف N writers منطقيًا ثم contract.',
 'كل إصدار يقرأ كتابة الآخر؛ race لا يفقد البيانات؛ contract بعد N retirement فقط.',
 json.dumps(detail('expand-dual-compatible-backfill-contract'),ensure_ascii=False),
 'N/N+1 ممثلان باستعلامين وليس نشر OCI rolling فعلي؛ fixture صغير، لا long-running transaction/replica/fleet proof. Contract backward-incompatible عمدًا بعد retirement؛ ليس rollback عامًا ممكنًا دائمًا.',
 'اجعل expand/contract releases منفصلة؛ controlled migration runner وlock_timeout ومراجعة fleet compatibility شرط تنفيذ الترحيل الإنتاجي.')
report('16-BACKUP-RESTORE.md','Backup / Restore Report','CONDITIONAL','',
 'إثبات restore محلي مستقل ومصالحة بعده بدل اعتبار dump نجاحًا كافيًا.',
 'pg_dump18.2→isolated DB restore يعيد محتويات كل الجداول وGL/stock/value؛ process restart يحافظ على facts.',
 f"{backup['tables']} جداول؛ synthetic private object؛ original poc preserved وpoc_restore أعيد إنشاؤها محليًا.",
 'python3 scripts/backup_restore.py: dump، fingerprint كل table، restore --exit-on-error، قارن counts/hash، reconcile، احذف مصدرobject-fixture ثم استعده، أوقف/أعد DB محليًا وافحص availability والمحتوى.',
 'تطابق كامل؛ zero GL/stock/value mismatches؛ object SHA256 نفسه؛ restart يعيد نفس facts.',
 f"PASS local: {backup['tables']} tables؛ GLdiff={backup['glDifference']}، stock={backup.get('stockMismatches')}، valuation={backup.get('valuationMismatches')}. dump {backup['backupSeconds']:.3f}s، restore+check {backup['restoreAndCheckSeconds']:.3f}s، restart+verify {backup['restartAndVerifySeconds']:.3f}s.",
 'هذه أزمنة fixture لا RPO/RTO. Logical snapshot بدون WAL/PITR. role reused في cluster نفسه؛ KMS/managed object versions/IdP config/provider already-accepted outcomes غير مستعادة. خدمة cloud/region لم تعتمد بعد.',
 'Local approach validated؛ **Cloud restore/SLA readiness remains pending**. لا تدّع end-to-end disaster recovery أو تنشئ موارد مدفوعة دون اعتماد منفصل.',
 '[Full before/after fingerprints](../evidence/backup-restore.json).')
report('17-SECURITY-NEGATIVE.md','Security Negative Test Report','CONDITIONAL','security',
 'إثبات أن محاولات السلبية تفشل، وتحديد مستوى الاختبار لكل حد.',
 'RLS/scoped keys + current authorization + strict schemas/DTO fields ترفض الهجمات المحددة.',
 'A/B/C1/C2، missing/malformed context، foreign resource IDs، deny after replay، unknown JSON properties، forbidden cost، bad MIME/size/hash.',
 'نفذ negative cases في SQL runtime وcontract functions وBFF fixture. valid tokens تحت removed membership وdisabled state لا تصرح.',
 'cross reads0؛ writes42501؛ modified ID0؛ mass assignment JsonException؛ invalid upload rejected؛ replay authorization denied؛ protected server DTO excludes cost.',
 'كل negative checks المنفذة نجحت. Contract mass assignment/upload tests هي function-level؛ tenant/company tests DB-level؛ BFF field/auth tests HTTP-level.',
 'ليس penetration test. لا full multipart/object scan، full permission/field search/sort/export، real support impersonation approvals أو production OIDC/MFA. generic SQL غير exposed؛ RLS لا يمنع compromised runtime من إعادة ضبط GUC.',
 'Security foundation مشروط بالمحافظة على الحدود المختبرة. امنع production auth/upload/support features حتى evidence لمسارها الكامل.')
report('18-RECONCILIATION.md','Reconciliation Report','PASS','reconciliation',
 'اكتشاف كل اختلاف مالي/مخزني في النماذج المنفذة بدون إصلاح صامت.',
 'مصادر facts append-only تُطابق projections والمجاميع بعد كل حركة وبعد restore.',
 'كل pools بما فيها افتتاحيات reservation، MWA، late cost، FG policies، partial allocation fixture، journals.',
 'ReconcilePool بعد كل حركة/سباق؛ SUM(stock qty/value)، separate valuation ledger وInventory GL؛ GLdebit-credit؛ allocation bound؛ WIPfacts/GL عند close؛ fingerprints بعد restore.',
 'zero unexplained difference؛ WIP policy رفضت partial schedule تبقى مفتوحة مع WIP160 معلن، لا close وهمي.',
 f"Restore scan: stock mismatches={backup.get('stockMismatches')}، valuation mismatches={backup.get('valuationMismatches')}، global GL difference={backup['glDifference']}. Estimated/Cap closed WIP=0؛ partial allocation original100=allocated100+remaining0.",
 'AR fixture أحادي debit item مع partial amounts؛ لا يمثل AP أو payment endpoints متعددة العملات أو control-account AR/AP reconciliation كاملًا. FX numeric vector ليس settlement flow. ستحتاج full typed debit-credit allocation vectors قبل AR/AP production features. Complete downstream costing graph pending.',
 'احتفظ بالمصالحة كfailure واضح. اختلاف earlier reservation setup كان fixture imbalance، عُولج بإضافة **opening ledger entries** ثم أعيد تشغيل كل السباقات/restoration، ولم تُعدّل facts المنشورة في المكان.')
write('19-FAILED-ASSUMPTIONS.md','''# Failed Assumptions Register — V1

| ID | Assumption | Evidence / actual observation | Impact / disposition |
|---|---|---|---|
| FA01 | PostgreSQL default rounding equals C# default | round(2.5)=3 vs ToEven=2; decimal vectors | Rejected. Explicit round_even policy and quantization required; legal rounding remains unverified |
| FA02 | .NET decimal safely holds every intermediate product | 24-digit ×24-digit fixed values overflow; exact numerator48 digits | Rejected. Rational/BigInteger intermediate + bounded final conversion; no decimal-only multiplication policy |
| FA03 | Estimated partial cost can never make WIP negative | estimate12, actual10, partial receipts yield -40 then-200 | Rejected. Manufacturing blocked for financial policy approval |
| FA04 | Forbid credit WIP without changing schedule or partial values | funded-estimate receipt30 rejected with WIP320<360 | Rejected. Cap/deferral/control policy must be explicit |
| FA05 | Worker lease fencing prevents all duplicate deliveries | crash after consumer commit creates second delivery, one effect | Rejected. At-least-once + Inbox, no exactly-once claim |
| FA06 | Valid ID token implies ERP permission/tenant claim trustworthy | B tenant_hint ignored; removed membership/disabled→403 | Rejected. Current ERP grants required |
| FA07 | Available official source metadata proves downloadable runnable artifacts | PG18.5 manifest unknown; Flutter archive403 | Rejected. PoC PG18.2 digest verified; production patch / mobile pair pending |
| FA08 | Quartz3 bootstrap/job signatures apply unchanged to Quartz4 | StdSchedulerFactory missing; Execute requires CancellationToken | Rejected. Corrected using installed package's XML docs and QuartzSchedulerBuilder; runtime probe passed |
| FA09 | Seeding reservation balance alone is sufficient for all-ledger reconciliation | Earlier fixture had100 stock rows without opening facts | Rejected. Opening stock/value/GL entries added; strict all-pool restore reconciliation now zero mismatches |
| FA10 | Successful local backup establishes cloud DR/SLA | local snapshot restored; keys/IdP/PITR not exercised | Not established. Cloud operations/release gate pending |

Harness fixes retained in evidence: initial incorrect big-numerator threshold (10^50 vs true48digits), changing
HttpClient.Timeout after use, missing OIDC iat in fixture, asynchronous Angular DOM check, and existing dev-server
port during npm ci. These were corrected and rerun, not silently counted as successful first runs.
No known remaining failing executed checks. Unexecuted scenarios remain PENDING/CONDITIONAL in their reports.
''')
write('20-UPDATED-ADRS.md','''# Updated ADR Register — PoC V1

This register appends evidence; it does not retroactively edit approved V1 baselines or self-approve new financial policy.
Statuses: **Validated for tested PoC scope** or **Proposed / Pending approval**, not blanket production Approved.

| ADR | Context / decision | Status | Evidence | Alternatives / consequences / revisit |
|---|---|---|---|---|
| POC-01 | One Npgsql local transaction explicitly shared by module DbContexts | Validated for tested scope |03,07| Separate local SaveChanges commits rejected; rerun on provider/pool changes |
| POC-02 | Transaction-local tenant/company RLS, nonowner runtime, no missing-scope access | Validated for tested scope |04,17| App-filter-only rejected; GUC not identity, PgBouncer needs own proof |
| POC-03 | Atomic conditional update for simple one-bucket reserve; ordered locks for multi-resource plan | Proposed implementation detail |05| Serializable/optimistic correct but retried; no throughput extrapolation |
| POC-04 | API receipt scope/fingerprint + permanent source-effect uniqueness | Validated for tested scope |06,07| TTL alone insufficient; external effect requires its own idempotency/reconciliation |
| POC-05 | Explicit quantization and bounded rational intermediates | Proposed financial numeric policy, technically validated |08| default PG round/decimal-only arithmetic rejected; legal overrides remain Pending Verification |
| POC-06 | Base MWA from locked Q/V, final issue residual, immutable corrections | Validated base; downstream method Pending |09| no average field as truth; multi-source graph/price adjustment still blocked |
| POC-07 | No final partial-production cost policy selected | Pending finance approval |10| estimate with controlled credit vs cap vs receipt deferral; manufacturing blocked |
| POC-08 | Outbox at-least-once + stable ID + atomic inbox/effect + fenced ACK | Validated for tested scope |11| independent broker not needed for demonstrated cases; no exactly-once claim |
| POC-09 | BFF code+PKCE, ERP grants separate from IdP | Conditional |12,17| managed IdP/MFA/rotation/TLS still required; no production auth readiness |
| POC-10 | Angular22.2.3 + TS6.0.3 + Node24.19.0; native HTML grid first | Validated technical feasibility |01,13| no commercial component adopted; a11y/browser scale gates before product UI |
| POC-11 | Flutter exact SDK/Dart pair remains unselected | Pending |01,14| no Flutter code before gate; mobile launch criticality OPEN |
| POC-12 | Expand/backfill/contract after N retirement; isolated restore rehearsal | Validated local / cloud Conditional |15,16| no automatic rollback promise after contract; no cloud SLA until cloud drill |
| POC-13 | Quartz4 bootstrap differs; RAM trigger compatible | Validated compatibility only |01,compat test| persistent store/clustering remain pending; timing is not business authority |
| POC-14 | Local OTel export works; managed collector pending | Validated compatibility only |01,compat test| no unaudited PII/secrets export; exporter/redaction review before operations |

Binding approved principles retained: modular monolith/local ACID; company/product/book MWA;
immutable posted ledgers; scoped owner ports; no independent broker/Kubernetes/cache introduction.
All compliance decisions listed in previous baseline remain Pending Verification.
''')
write('21-UPDATED-RISKS.md','''# Updated Risk Register — PoC V1

| Risk | State / evidence | Severity | Owner | Mitigation / gate |
|---|---|---|---|---|
| R01 Lost local posting effect | Reduced by ACID + deferred effects/balance tests | Critical | Backend/Finance | preserve one UoW, source uniqueness, rerun after persistence changes |
| R02 Cross-tenant/company pooling leak | No leak in tested runtime/reuse/cancel paths | Critical | Security/Platform | deployment role gate, verified scopes, no generic SQL; pool/proxy changes require rerun |
| R03 Overselling reservations | 100 pair races, no double win | Critical | Inventory | ordered multi-resource/expiry/release tests before full reserve feature |
| R04 Undetected decimal mismatch/overflow | Defaults differ; explicit policy vectors pass | Critical | Finance/Backend | bounded rational intermediates and legal rounding review |
| R05 WIP credit policy/uneven output cost | OPEN | Critical | Financial controller/Manufacturing | approve estimate/cap/deferral; manufacturing blocked |
| R06 Multi-source late cost and downstream graph | UNPROVEN | Critical | Costing/Architecture | bounded graph, stale cutoff, concurrency and finance review; affected posting features blocked |
| R07 Real OIDC/MFA/session revocation | Local protocol fixture only | High | Identity/Security | choose managed test tenant; MFA, refresh rotation, outage/TLS browser tests |
| R08 Flutter/Dart/plugins/device capability | Official archive403; no execution | High | Mobile/Platform | allow official host, exact pair/checksum, Android+iOS devices; mobile implementation blocked |
| R09 Cloud restore/keys/PITR/external accepted outcome | Local19-table restore only | Critical before launch | SRE/Security/Integrations | provider/region chosen then paid-resource approval and end-to-end drill |
| R10 Licenses/notices for native/build/mobile/vendor dependencies | Declared installed licenses identified; MPL2/CCBY present | Medium | Legal/Engineering | preserve notices; review distributed artifacts, vendor terms/plugins before adoption |
| R11 Browser/accessibility/scale | Chromium100rows only | Medium | Web/QA | screen reader, keyboard audit, target-browser and large-grid cases |
| R12 AR/AP typed allocation FX/control reconciliation | Numeric/partial debit fixture only | High before feature | Finance/QA | two-ended allocations, overpayment, FX, AP and reversal tests |
| R13 Migration lock/fleet/long transaction | Small local N/N+1 SQL fixture only | High | Platform/Backend | controlled runner, canary, lock budgets, actual rolling proof |
| R14 Current supported production servicing patch | PG18.2 PoC; 18.5 image absent | High before deployment | Platform | select managed supported patch, digest/restore regression; no latest policy |
| R15 Jordan/JoFotara/retention/residency/transfers | Pending Verification unchanged | Critical before legal workflows | Compliance/Local counsel | official evidence and separately approved contracts; no inferred legal signoff |
| R16 Quartz scheduler store + OTLP provider | Quartz RAM/OTel local exporter only | Medium | Platform | real persistent job-store and collector checks before operational claims |

No new production dependency or paid service is authorized by this risk register.
''')
failed=[t for t in tests if t['status']=='FAIL']
decision='CONDITIONAL GO' if not failed and all(c['status']=='PASS' for c in browser['checks']) and backup['status']=='PASS' else 'NO-GO'
write('22-READINESS-SCORECARD.md',f'''# Implementation Readiness Scorecard — V1

## Final Decision: **{decision}**

{len(tests)} backend checks executed, {len(failed)} failed; {len(browser['checks'])} browser checks passed;
isolated local restore and database restart passed. No paid cloud resources were created.
This recommendation is **not approval to begin implementation**. The user approval gate remains in force.

| Exit gate | Evidence | Judgment and boundary |
|---|---|---|
| A Financial Integrity |03,06,07,08,09,18| PASS for modeled ACID/idempotency/balance/decimal/base-cost invariants. CONDITIONAL beyond them: production costing/late dependency graph/complete AR/AP allocations remain blocked |
| B Inventory Integrity |05,09,18| PASS for single-bucket concurrency, base stock/value/GL consistency and bounded retry; multi-resource reserve cases needed before full feature |
| C Tenant Security |04,12,17| PASS for trusted-context runtime RLS/pool/company/worker isolation; managed IdP and full command/field/upload/support security remain CONDITIONAL before corresponding feature readiness |
| D Operational Reliability |11,15,16| PASS for local crash/timeout/outbox recovery, mixed SQL versions and disposable restore. Cloud PITR/keys/IdP/accepted external outcomes/SLA PENDING |
| E Technology Compatibility |01,02| PASS for executed .NET/EF/Npgsql/PG18.2 and Angular/TS tuple, identified critical licenses. Production servicing patch re-pin required; Flutter/Dart separate PENDING gate |

### Scope that may be proposed for implementation

After separate approval: **Implementation Foundation — V1 (Backend/Web)** only: module/contract boundaries,
CI and locked dependencies, verified request scope and RLS/UoW plumbing, audit/idempotency/outbox primitives,
test harness integration and a Web shell. No full ERP features are authorized by this sprint.

### Explicit blockers / excluded work

1. Manufacturing and final production cost: financial choice of temporary WIP policy; downstream cost proof.
2. Multi-source late cost adjustments and posting workflows requiring them: bounded graph, stale plan and downstream reconciliation.
3. Mobile: exact Flutter/Dart pair, plugin licenses, real-device scanner/secure storage/OIDC/notification proof.
4. Production authentication: real managed IdP, MFA, session/refresh rotation/revocation, service grants and browser TLS settings.
5. Full AR/AP allocation/FX and complex reservations: typed end-to-end feature tests before their implementation exits validation.
6. Cloud deployment/production launch: provider-supported servicing patch, PITR/keys/objects/identity recovery, RPO/RTO and compliance evidence.

Mobile launch criticality is still OPEN. Restricting the proposed foundation to Backend/Web does not silently remove Mobile
from the launch scope or claim a complete launch plan. No production-readiness or legal-compliance GO is issued.

### Next stage (proposal only)

**Implementation Foundation — V1, with the exclusions above and blocking work tracked separately.**
Wait for explicit user approval. If approval instead targets closing the unresolved cost/identity/mobile gates,
those experiments can be scoped as a separate validation extension; do not start them or build product automatically.
''')
write('23-FAILURE-INJECTION.md','''# Failure Injection Matrix — V1

| Injected fault | Expected state | Actual state / evidence test | Recovery | Duplicate effect? | Manual intervention? |
|---|---|---|---|---|---|
| Process killed after posting commit before response | committed receipt once | process-killed-after-commit-replayed-once PASS | replay same intent | No | No |
| Worker process killed before dispatch | durable pending lease, no consumer effect yet | outbox-real-worker-process-kill-before-and-after-consumer PASS | lease expires; another worker | No | Test accelerates lease clock only |
| Worker process killed after consumer commit before ACK | event redelivered, effect once | same real-process test PASS | duplicate consumer hit inbox | delivery duplicated, effect No | No |
| Backend connection terminated mid-transaction | no business commit | connection-interruption... PASS | new full UoW | No | No |
| PostgreSQL deadlock | one40P01 victim | postgres-deadlock... PASS | fresh UoW consistent lock order | No | No |
| Serializable write contention | 40001 and bounded retry | reservation-serializable PASS; counters in results | restart whole transaction | No double reserve | No |
| DB command timeout | transaction aborted/rollback, scope not reused | database-command-timeout... PASS | rollback; new scoped worker | No | No |
| Database stopped | connection unavailable, committed facts retained | backup-restore databaseOutageObserved=true | start own disposable DB; verify all fingerprints | No | Local test controls service lifecycle |
| Duplicate command / changed fingerprint | replay / conflict | idempotency tests PASS | same receipt / reject conflict | No | No |
| External API timeout after effect | outcome may be unknown | external-timeout... PASS | stable provider key, retry idempotent stub | two sends, one effect | Real nonidempotent provider may need reconciliation |
| Stale lease ACK | zero rows | outbox-crash... PASS | current lease ACK only | No | No |
| Stale revision | zero update | stale-concurrency-token... PASS | explicit conflict, never auto-overwrite intent | No | No |
| Consumer payload hash conflict | visible reject | inbox.payload_conflict expected in outbox test | investigation; no silent accept | No extra effect | Yes if real incident |
| Unbalanced/required accounting missing | whole UoW rejected | accounting tests PASS | correct intent/config then new allowed attempt | No | User/config intervention needed |

Not executed: actual network partition during COMMIT ACK (process-kill after commit is the exercised ambiguity window),
managed IdP outage/rotation, cloud-zone failure, real provider accepted-state replay after backup restore.
These remain pending; the table does not substitute modeled lease expiry for physical crashes where not performed.
''')
index='''# PoC & Technical Validation Sprint — V1

تم تنفيذ تجارب مؤقتة داخل المستودع، دون بناء منتج ERP أو إنشاء موارد مدفوعة.
'''+f'**القرار الوحيد: {decision}.** نجح {len(tests)} اختبار Backend و{len(browser["checks"])} فحص متصفح، والاستعادة المحلية؛ لا إخفاقات متبقية في الاختبارات المنفذة.\n\n'+'''التفاصيل والنطاق المسموح باقتراحه والـBlockers في [Readiness Scorecard](22-READINESS-SCORECARD.md).
نجاح الاختبارات المحددة لا يعني اكتمال جميع السيناريوهات الإنتاجية. التصنيع/late-cost graph، الهوية المُدارة،
الموبايل والاستعادة السحابية بقيت مشروطة أو Pending كما هو موثق.

| Requested output | Artifact |
|---|---|
'''
for i,path in enumerate(sorted(R.glob('[0-9][0-9]-*.md')),1):
 if path.name.startswith('23-'):continue
 index+=f'| {path.stem} | [{path.name}]({path.name}) |\n'
index+='''
Additional: [failure injections](23-FAILURE-INJECTION.md)، [raw final results](../evidence/results.json)،
[source manifest](../evidence/official-source-manifest.json)، [full licenses](../evidence/resolved-license-matrix.csv).
[Cloud environment setup / saved draft](ENVIRONMENT-SETUP.md).

Reproduce: `python3 scripts/bootstrap.py`, `bash scripts/run.sh`, `bash scripts/web.sh`, then
`python3 scripts/reports.py`. Use only disposable local services; run.sh resets `poc` on its named test container.
Frozen dependency locks are included. Evidence records the exact run time and environment; logs from failed earlier
harness revisions are preserved separately and never counted toward the final passed checks.

Approval record: the uploaded task explicitly approved the Blueprint and authorized only this PoC sprint.
No implementation started. Proposed next stage: **Implementation Foundation — V1 (Backend/Web), with explicit exclusions**.
Await user approval before creating production foundation or product features.
'''
write('INDEX.md',index)
print(json.dumps({'decision':decision,'backend':len(tests),'failed':len(failed),'browser':len(browser['checks']),'reports':len(list(R.glob('*.md')))},indent=2))
