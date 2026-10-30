# Configuration and secrets

| Setting | Source / purpose |
|---|---|
| `ERP_RUNTIME_CONNECTION` | Environment secret; nonowner, NOSUPERUSER, NOBYPASSRLS; bounded pool |
| `ERP_MIGRATION_CONNECTION` | Controlled migration host only; never API/Worker |
| `ERP_DEV_AUTH_TOKEN` | Random local/test token, minimum24chars; Development/Test + loopback only |
| `ConnectionStrings:Runtime` | Test-host configuration override; never commit a real connection string |
| `DevelopmentIdentity:Enabled` | Default false; enabling outside Development/Test throws during registration |
| `DevelopmentIdentity:Subject/Grants` | Server-side synthetic membership; selectors must match tenant/company/action |
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` | Local runner explicitly sets Development; production does not enable test identity |
| `POC_TOOLS` | Optional writable cache location; env.sh reuses verified existing tooling or `.local/tools` |
| `POC_CHROMIUM` | Local browser executable for tests only; CI uses package-matched installed browser |

`ISecretProvider` allows only registered names and never returns configuration dumps. Local random values live in ignored
`.local/settings.json` (0600); `local.py` removes migration credentials before spawning runtime hosts. No secrets in command
arguments, structured logs, trace labels or repository files. Cloud secret/KMS adapter is not selected or implemented.

Nonlocal DB connections require `SslMode=VerifyFull` and trusted CA configuration. No TrustServerCertificate bypass.
Npgsql parameter/sensitive EF logging is not enabled. Runtime startup guard rejects owner/superuser/BYPASSRLS,
missing migrations or missing FORCE RLS. Readiness checks platform version metadata only, an explicit global metadata exception.

No production IdP issuer/client secret/endpoints were invented. Without a production adapter, protected endpoints deny requests.
The Angular AuthenticationSession defaults to no authenticated session, permissions or stored token. UI visibility is never authority.
Exporter destination/credentials remain intentionally unset; instrumentation and exporter DI hooks are implemented and tested locally.

`python3 scripts/local.py finance-demo` prepares only the local development identity. Create
the JOD book, Open period and accounts through their Web setup screens after API/Web start;
the script does not seed any of them. Existing settings, books and Closed periods are
preserved. This is not a production onboarding policy.
Local grants independently list Finance.Journal.View/Create/Edit/Post/Reverse and
Finance.Period.View/Create/Close, Finance.Account.View/Create and Finance.Book.View/Create.
`/context` returns only the authenticated principal's current permissions for the verified tenant/company. The Web session
accepts a local token in a password field, verifies context through the API, and retains token/permissions only in memory.
Reload ends the session; language preference alone is persisted. API authorization remains authoritative on every request.

The golden browser runner creates its own disposable PostgreSQL container and random credentials, restricts forwarding to
loopback and tests real API/Worker hosts. `ERP_TEST_API_ORIGIN`, `ERP_WEB_PORT`, `ERP_UI_ORIGIN`, `ERP_E2E_TOKEN` and
`ERP_BUILD_CONFIGURATION` are test-process settings only; they are not production configuration or a public proxy.
