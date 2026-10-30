# Disposable PoC workflow

Use the existing checkout `/workspace/codex`; do not create a Git worktree unless explicitly requested.
This folder contains user-authorized throwaway PoC code, not production ERP code. Prior design baselines
are `/workspace/erp-design`; the current decision and blockers are in `reports/22-READINESS-SCORECARD.md`.
No paid cloud resources, production implementation, pushes or deployment are authorized.

1. From `/workspace/codex/poc-v1`, run `bash scripts/install.sh` if tooling needs refresh.
   It verifies SDK archive SHA512, uses SDK10.0.402, locked NuGet restore, npm ci and builds both targets.
   Requires existing Docker, Node24.19.0/npm11.9.0 and Chromium (actual environment versions in evidence).
   .NET named mutex creation may need the tool's elevated local-execution path in this sandbox;
   do not disable signature/checksum/TLS verification or change source to avoid this environment limitation.
2. Run `bash scripts/run.sh`. It starts `erp-poc-v1-pg` on loopback55432 with a purpose label;
   it resets only this disposable PoC schema. Readiness includes pg_isready, real EF/Npgsql checks,
   and isolated backup/restore. The script preserves the backend exit code and stops on setup errors.
3. Run `bash scripts/web.sh`. Port4207 must be free. It uses a temporary production-build file server,
   tests the Angular screen with Chromium, then stops only its own server.
4. Run `python3 scripts/reports.py`; inspect actual failures in evidence before treating any gate as passed.
5. Keep `erp-poc-v1-pg` for subsequent tests, or explicitly `docker stop erp-poc-v1-pg` after use.
   Processes do not survive environment publication/restoration. Re-run the startup command in a new task.

No external credentials are needed for current local tests; credentials in the schema/container are synthetic,
disposable and must never be reused for a real deployment. OIDC is a local protocol fixture, not a real IdP.
Flutter/Dart archive access and mobile/device work remain PENDING. Managed IdP/MFA/rotation, cloud PITR/KMS,
late-cost propagation and manufacturing policy are separate blockers. Test pass counts do not clear those gates.

Environment configuration is a review draft until the user saves/publishes it. PoC files are local/uncommitted;
a fresh Git clone from the currently empty remote does not contain them. Preserve the working snapshot or review
and commit these artifacts separately before expecting a fresh clone to reproduce the workflow.
