# Compatibility and dependency governance

| Component | Foundation pin | Constraint / evidence |
|---|---|---|
| SDK / ASP.NET |10.0.402 /10.0.13|global.json, official SDK SHA512; .NET10 LTS |
| EF Core / Relational / Design |10.0.13|aligned explicitly including transitive consumers |
| Npgsql / EF provider |10.0.3|provider EF>=10.0.4,<11; tested with10.0.13 |
| PostgreSQL |18.2 + OCI digest|development/test pin, not final supported production servicing decision |
| Angular core/common/compiler/router/platform/CLI/build |22.2.3|exact npm peers Node^24.15.0 and TS>=6,<6.1; Vite override8.3.5 fixes current launch-editor advisories |
| TypeScript |6.0.3|7.0.2 excluded |
| Node / npm |24.19.0 /11.9.0|actual tuple; engines strict |
| OpenTelemetry Hosting/ASP.NET |1.20.0|HTTP/Npgsql/worker/outbox instrumented; test exporter captures shared trace |
| Testcontainers PostgreSql |4.16.0|real Docker-backed test infrastructure |
| Playwright |1.64.1|local Chromium154 override recorded; CI installs matched package browser |
| xUnit / VS runner / Test SDK |2.9.3 /3.1.5 /18.0.1|new test-only dependencies, restored and executed |
| Vitest / jsdom / Prettier |4.1.12 /27.0.1 /3.6.2|Angular builder supports Vitest^4.0.8; test/build tools only |

Official primary evidence: [.NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json),
[Angular compiler exact peers](https://registry.npmjs.org/@angular/compiler-cli/22.2.3),
[provider nuspec](https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/10.0.3/npgsql.entityframeworkcore.postgresql.nuspec),
[Angular build runner peers](https://registry.npmjs.org/@angular/build/22.2.3).
Earlier [PoC matrix](../poc-v1/reports/01-COMPATIBILITY.md) remains historical evidence, not a substituted runtime test.

Governance: central `Directory.Packages.props`, global.json, tool manifest, per-project packages.lock.json, exact npm versions and
package-lock.json; CI uses --locked-mode/npm ci, `check-dependencies.py`, and moderate-or-higher dependency gates. Upgrades are
intentional reviewed changes to the tuple/locks, with architecture, isolation, transaction, migration and client regressions.
No automatic dependency merge or random `latest` policy. Do not suppress audit findings or silently relax peer ranges.

Core licenses: .NET/EF/Angular MIT, Npgsql/PostgreSQL PostgreSQL License, OTel/Playwright/TypeScript Apache-2.0;
xUnit/runner Apache-2.0; Testcontainers/Vitest/jsdom/Prettier MIT; Test SDK MIT. Native/build dependencies include MPL2/CC-BY notices;
preserve exact package notices and review redistributed artifacts. No paid grid or mobile plugin was adopted.
Quartz4.5.1 was verified by the PoC but is not a dependency of this foundation worker: bounded polling requires no calendar
scheduling. Add pinned Quartz when an approved scheduled use case/store validation justifies it. Flutter/Dart stay PENDING and uninstalled.

Security gate selected Vitest4.1.12 after audit rejected4.0.18 (GHSA-5xrq-8626-4rwp, GHSA-82fw-gwwq-j7x9). The corrected version remains within Angular peer ^4.0.8; no range was relaxed.
The 2026-10-21 gate pinned transitive Vite8.3.5 because Angular build22.2.3 declares Vite8.3.0 and npm resolved a vulnerable
8.3.2 under GHSA-698v-4r3x-3vcv, GHSA-r825-w8mm-5pxw and GHSA-587h-7wmj-h5pm. The override stays within the same Vite8
builder line; the full build/test/browser gate and a zero-finding npm audit validate the tuple.
# Manual Journal compatibility note

The Finance slice uses the same approved .NET/EF/Npgsql/PostgreSQL/Angular/TypeScript tuple below. No new package version,
grid library, money library or database extension is introduced. Three SQL lock functions are native PostgreSQL features;
client totals use native BigInt and server money uses decimal. Exact dependency locks and security gates remain enforced.
