# ERP Technical Validation Sprint V1 — disposable experiments

This is throwaway technical validation, not an ERP implementation. All identities, accounts,
documents and credentials are synthetic. No legal/tax compliance or production suitability is claimed.
Reports and machine evidence distinguish executed checks from pending external/device checks.

Prerequisites: verified .NET SDK 10.0.402, Node 24.19.0, npm 11.9.0, Docker daemon,
PostgreSQL image pinned in `scripts/run.sh`, Chromium. No paid cloud resources are required.

Run `bash scripts/run.sh` from this directory. It starts only a named disposable PostgreSQL
container, resets only its `poc` schema, builds and executes the C# harness, and retains reports.
`bash scripts/web.sh` runs the Angular build and browser tests. Do not run against a real database.
See reports/INDEX.md for actual outcomes and limitations. Tooling is cached outside the checkout
under `/workspace/erp-poc-v1/tools`; install instructions are in scripts/bootstrap.py.

RLS here protects against omitted query predicates with trusted verified context. PostgreSQL
custom settings are not authentication: a compromised runtime able to execute arbitrary SQL can
change them. Never expose a generic SQL endpoint or treat RLS as a substitute for authorization.
