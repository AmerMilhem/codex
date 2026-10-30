# OIDC Report

**Result: CONDITIONAL** — نطاق الحكم هو التجربة الموصوفة فقط. تاريخ الدليل: 2026-10-18T09:55:59.9923324+00:00.

## Objective
اختبار Minimal BFF auth عبر ASP.NET middleware مع تفويض ERP حالي.

## Hypothesis
Authorization code+PKCE/state/nonce والتحقق من token لا يساوي ERP authorization أو membership.

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
RSA local issuer، user-1، service-1، token يحوي tenant_hint غير مخول؛ cookie session؛ enabled/membership flags.

## Steps
شغّل discovery/JWKS/authorize/token/logout fixtures وBFF منفصلًا؛ اتبع login redirects؛ افحص PKCE، cookie، CSRF، expiry/audience/signature وعضوية مسحوبة ثم logout؛ جرب refresh/client credentials.

## Expected Result
token الصحيح لا يصرح بعمل ERP تلقائيًا؛ claim tenant لا يُعتمد؛ disabled/removed→403؛ bad token denied؛ logout→401.

## Actual Result
{"login": "authorization code + PKCE, state and nonce via ASP.NET middleware", "pkceValidated": 1, "tokenNegativeCases": 3, "csrf": "missing rejected, correct accepted", "tenantSource": "ERP membership, not token tenant_hint", "removedMembership": "403", "disabledUser": "403", "logout": "401", "refreshes": 1, "serviceAccount": "token verified, no automatic ERP grant", "provider": "local RSA protocol fixture", "mfa": "PENDING real provider enrollment/challenge", "refreshSession": "token endpoint exercised; provider rotation/reuse/revocation and automatic BFF renewal pending", "productionTls": "PENDING; HTTP limited to loopback fixture"}

| Test | Result | ms |
|---|---|---:|
| `oidc-code-pkce-bff-current-membership-negative-tests` | PASS | 371.90 |



## Evidence
[النتائج المقروءة آليًا](../evidence/results.json) تتضمن اسم الاختبار، نتيجته، مدته والتفاصيل الفعلية.
[سجل التشغيل النهائي](../evidence/final-run.log)، [اختبارات المتصفح](../evidence/angular-browser.json)،
[الاستعادة](../evidence/backup-restore.json). الكود مؤقت وقابل لإعادة التنفيذ؛ لا تُعد الوثيقة وحدها إثباتًا.

## Failure Analysis
الـissuer بروتوكولي محلي وليس managed IdP؛ لا شاشة إدخال اعتماد/MFA حقيقية. refresh endpoint نجح، لكن automatic BFF renewal/rotation/reuse detection/provider logout/revocation/outage لم تثبت. Service account concept فقط؛ لا M2M authorization كامل. HTTP loopback وSameSite Lax/query للـfixture لا يُنقلان إلى production TLS/config.
السجلات السابقة محفوظة في `evidence/backend-first-run.json` و`backend-second-run.json` و`evidence/runs/`؛
نتائجها ليست نتيجة الجولة النهائية. PASS لا يشمل سيناريو لم يُنفذ.

## Recommendation
اختيار provider/test tenant ثم MFA enrollment/challenge/session rotation/revocation/issuer outage، BFF TLS/CSRF/browser cookies وmobile PKCE شرط قبل authentication production readiness. Business grants تبقى في ERP.
