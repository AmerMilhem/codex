# Local development

ابدأ من جذر المستودع الموجود؛ لا تنشئ worktree جديدًا إلا إذا طلب المستخدم.
راجع النسخ المثبتة في [Compatibility](COMPATIBILITY.md). `bootstrap.sh` يستعمل locked restore وnpm ci؛ لا يعدّل source/locks.

1. `bash scripts/bootstrap.sh` ثم `source scripts/env.sh`.
2. `dotnet build Erp.slnx --no-restore -m:2 -nr:false`.
3. `python3 scripts/local.py` ينشئ فقط Docker service المسماة `erp-foundation-dev` والـnamed volume المحلي،
   ثم identities منفصلة وmigrations. يعيد التشغيل دون reset أو حذف بيانات.
4. `python3 scripts/local.py api`؛ endpoint الفعلي `/health/ready` يجب أن يعيد200.
5. `python3 scripts/local.py worker`؛ polls محكومة بالسياقات المحلية، ولا handler أعمال مسجل. unknown event→Failed واضح.
6. `npm --prefix src/Clients/Web start`؛ dev proxy يوجه `/api` و`/health` إلى5080. لا user-facing preview URL منشورة من أداة الإعداد.

للـsmoke test المصرّح به استخدم `python3 scripts/smoke-local.py`؛ يقرأ token من الملف المحلي في الذاكرة ويرسل
طلب context إلى company grant الاصطناعي. لا تضع token أو connection strings في سطر أوامر curl أو ملفات tracked.

إيقاف API/Worker بـCtrl+C. إيقاف DB مع الاحتفاظ بالبيانات:

```bash
docker stop erp-foundation-dev
```

لا تستخدم `down -v` كإعادة تشغيل عادية؛ حذف volume عمل منفصل يفقد بيانات التطوير. احفظ `.local/settings.json`
متوافقًا مع volume؛ إذا فقد secrets file لا ينشئ السكربت كلمات مرور بديلة فوق roles موجودة بصمت.
كل credentials هنا تجريبية؛ لا بيانات حقيقية أو موارد cloud مدفوعة.

Sandbox الحالي قد يمنع named mutex اللازمة لـdotnet؛ استخدم مسار execution المحلي المسموح عند الحاجة، ولا تعطل TLS/checksums/signature checks.
عمليات إعداد runtime لا تعني نشر snapshot. تبدأ العمليات مجددًا بعد استعادة بيئة جديدة؛ اختبر readiness من جديد.
# Manual Journal local walkthrough

After building the solution, run `python3 scripts/local.py finance-demo` once to prepare the development identity
without changing existing financial data. Run API/Worker as documented and Web via `npm start`. On `/workspace`, enter the tenant
`aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa`, company `11111111-1111-4111-8111-111111111111` and privately obtain the token from
`.local/settings.json`. The password field keeps it only in memory; never place the token in a URL or command argument.
The server returns allowed permissions. Open Manual journals and follow [the slice guide](MANUAL-JOURNAL-V1.md).
For a new local database, create a JOD book on Accounting Books, the desired Open date
range on Fiscal Periods, then the Cash and Expense accounts on Chart of Accounts.
`finance-demo` inserts none of these. Then create and post a journal within that period.
For a clean independent acceptance database/browser, run `python3 scripts/test-journal-e2e.py` after Release and Angular builds.
