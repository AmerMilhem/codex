# Verified Compatibility Matrix — V1

**Scope:** Selected and executed PoC tuple, not a deployment/security certification. PASS means the listed compatibility
was demonstrated under the stated constraint; unsupported scenarios remain explicit. No `latest` issuance policy.
Sources were fetched again during this sprint; hashes/status are in [official-source-manifest.json](../evidence/official-source-manifest.json).
Package restore and checksums stayed enabled. [SDK SHA512](../evidence/sdk-checksum.json).

| Component | Selected version | Compatible range | Support status | License | Known constraint | Result | Official evidence |
|---|---|---|---|---|---|---|---|
| .NET | SDK 10.0.402; runtime 10.0.13 | net10.0; fixed global.json | LTS 10; policy evidence, next servicing patch separately reviewed | MIT | SDK SHA512 verified; Linux x64 only | PASS | [official](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/README.md) |
| ASP.NET Core | 10.0.13 | shared framework + OIDC package 10.0.13 | follows .NET 10 LTS | MIT | Kestrel/OIDC loopback; managed TLS/IdP not proven | PASS | [official](https://raw.githubusercontent.com/dotnet/core/main/release-notes/10.0/README.md) |
| EF Core | 10.0.13 | net10.0; all resolved EF components aligned | 10.x LTS alongside .NET; keep patch alignment | MIT | 3 contexts explicitly enlisted; no implicit independent commit | PASS | [official](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore/10.0.13/microsoft.entityframeworkcore.nuspec) |
| Npgsql EF provider | 10.0.3 | EF Core/Relational >=10.0.4 <11.0.0; Npgsql >=10.0.3 | upstream stable package; no invented vendor SLA | PostgreSQL | observed 10.0.13 tuple; not every version in range tested | PASS | [official](https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/10.0.3/npgsql.entityframeworkcore.postgresql.nuspec) |
| Npgsql | 10.0.3 | net10.0 target tested | stable package; patch policy required | PostgreSQL | pool tested directly; PgBouncer not tested | PASS | [official](https://api.nuget.org/v3-flatcontainer/npgsql/10.0.3/npgsql.nuspec) |
| PostgreSQL | 18.2 + OCI digest pinned | 18.x selected; exact executed 18.2 | 18 major supported; freshest servicing patch availability unresolved | PostgreSQL | 18.5 image unavailable; 18.2 is PoC pin, not approved production patch | PASS | [official](https://raw.githubusercontent.com/postgres/postgres/REL_18_2/configure.ac) |
| Angular core/forms/compiler/CLI/build | 22.2.3 | Node ^22.22.3 OR ^24.15.0 OR >=26.0.0; TS >=6.0 <6.1 | 22 active major in official support table; no perpetual patch support claim | MIT | zoneless typed forms; no commercial grid adopted | PASS | [official](https://registry.npmjs.org/@angular/compiler-cli/22.2.3) |
| TypeScript | 6.0.3 | >=6.0 <6.1 for this Angular compiler | fixed compiler; no LTS promise | Apache-2.0 | 7.0.2 explicitly excluded | PASS | [official](https://registry.npmjs.org/typescript/6.0.3) |
| Node.js | 24.19.0 | satisfies ^24.15.0; Playwright >=20 | 24 LTS; schedule EOL 2028-04-30 | MIT plus bundled notices | actual preinstalled version, not previous 24.22 candidate | PASS | [official](https://raw.githubusercontent.com/nodejs/Release/main/schedule.json) |
| Flutter | UNSELECTED | bundled stable Flutter/Dart pair must be confirmed | PENDING exact supported stable release | BSD-3-Clause source only | official archive CONNECT 403; SDK not installed; no mobile PoC started | PENDING | [official](https://storage.googleapis.com/flutter_infra_release/flutter/releases/releases_linux.json) |
| Dart | UNSELECTED | must be bundled with selected Flutter, not pubspec range alone | PENDING paired release | BSD-3-Clause source only | neither stable branch constraint nor independent Dart latest proves pair | PENDING | [official](https://raw.githubusercontent.com/dart-lang/sdk/main/LICENSE) |
| Quartz.NET | 4.5.1 | net10.0 tested | stable 4.x candidate; no stated LTS/SLA | Apache-2.0 | Quartz 4 API differs: QuartzSchedulerBuilder; Execute includes CancellationToken; RAM store only | PASS | [official](https://api.nuget.org/v3-flatcontainer/quartz/4.5.1/quartz.nuspec) |
| OpenTelemetry | 1.20.0 SDK/Hosting/AspNetCore instrumentation | resolved graph on net10.0 | stable 1.x packages; instrumentation policy varies | Apache-2.0 | 1 local exported span; OTLP collector/hosted rollout pending | PASS | [official](https://api.nuget.org/v3-flatcontainer/opentelemetry/1.20.0/opentelemetry.nuspec) |
| Testcontainers.PostgreSql | 4.16.0 | net10.0 + Docker 28.4.0 / API 1.51 | stable test-only dependency | MIT | real disposable PG container functional probe; no production dependency | PASS | [official](https://raw.githubusercontent.com/testcontainers/testcontainers-dotnet/develop/LICENSE) |
| Playwright Node | 1.64.1 | Node >=20; managed browser normally tied to package | stable test tool, pinned | Apache-2.0 | tested installed Chromium 154.0.8037.57 via executablePath, not bundled revision; other browsers pending | PASS | [official](https://registry.npmjs.org/@playwright/test/1.64.1) |

Angular peer ranges are recorded verbatim from exact `compiler-cli@22.2.3`, not inferred from a tag or a major-version table.
Node 24.19.0 and TypeScript 6.0.3 were actually used for the successful build. RxJS 7.8.2 fits the declared peer range;
zone.js is optional and omitted under the tested zoneless configuration. 11 browser checks passed.

PostgreSQL image: `postgres:18.2@sha256:a688ae5c99f86c1424e046ace0de890608cc0ddb5ec8ef72a6ff8305fdc8f4d2`.
The attempted 18.5 tag returned manifest unknown. Official moving branch currently mentioning a later patch is not proof
that a distributable image was available. Re-pin and rerun against the vendor's supported current patch before a real deployment.
Flutter/Dart remain PENDING, not FAIL and not PASS. This blocks mobile dependency adoption; mobile launch criticality remains OPEN.
