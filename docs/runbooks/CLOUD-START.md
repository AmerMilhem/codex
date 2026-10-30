# Cloud task startup — Foundation V1

Use the existing checkout `/workspace/codex`; do not create a Git worktree unless the user explicitly asks.
This is the authorized Backend/Web Foundation. Read README.md and docs/evidence/FOUNDATION-V1.md first.
No Finance/Inventory/Sales feature, paid cloud resource, mobile implementation or production deployment is authorized.

1. `cd /workspace/codex` and `bash scripts/bootstrap.sh` for a dependency refresh. It uses SDK10.0.402,
   locked NuGet restore, a pinned dotnet-ef tool and npm ci with exact versions. SDK installation verifies official SHA512.
2. `source scripts/env.sh`; `dotnet build Erp.slnx --no-restore -m:2 -nr:false`.
3. `python3 scripts/local.py` to start the named local PostgreSQL service and apply controlled migrations.
   It preserves the named volume and ignored local credentials; it never resets ERP data or deletes the database.
4. `python3 scripts/local.py api` runs API on loopback5080; verify a real `/health/ready` request.
   In another process run `python3 scripts/local.py worker`; run `npm --prefix src/Clients/Web start` for the shell.
   Do not create a user-facing loopback preview link in onboarding UI; validate with local requests/browser tests.
5. `python3 scripts/smoke-startup.py` validates API/worker startup, scope rejection and a technical outbox probe,
   then stops only the processes it started. The local database remains available.
6. `POC_CHROMIUM=/usr/bin/chromium bash scripts/verify.sh` runs the complete acceptance gate using the local
   installed Chromium. CI instead installs the browser paired with Playwright1.64.1. Do not run dotnet format/build concurrently.

If .NET cannot create its named mutex in the command sandbox, use the supported elevated local execution path;
do not disable TLS/signature/checksum verification. No external identity credentials are needed for current local tests.
`ERP_MIGRATION_CONNECTION` belongs only to the controlled migration host; local.py removes it before runtime startup.
Never print `.local/settings.json`, tokens, connection strings or process environment values.

Live services do not survive snapshot restoration; restart them and recheck readiness. The source is currently local/uncommitted
in an initially empty remote. Preserve the reviewed workspace snapshot or commit separately before expecting a new remote clone
to contain it. Saving this setup is a configuration draft, not runtime activation/publication or proof of fresh-task restoration.

Production OIDC/MFA, mobile, WIP/late costing, full AR/AP/FX, complex reservations, cloud production/PITR and JoFotara remain blocked.
At Foundation completion stop and obtain the next explicit user gate before a product vertical slice.
