# License Matrix — V1

**Result: PASS for identifying licenses in the executed Linux dependency graph; distribution obligations remain CONDITIONAL.**

[Full installed dependency matrix](../evidence/resolved-license-matrix.csv): 268 packages,
33 NuGet and 235 npm. Missing/file-only expressions requiring identification: 0.
Direct components and official license links appear in [Compatibility](01-COMPATIBILITY.md). Flutter/Dart source licenses
do not clear unselected mobile plugins. Managed OIDC, cloud service terms, fonts, scanners and notification plugins remain unselected.

| Declared expression | Installed packages |
|---|---:|
| `(Apache-2.0 AND BSD-3-Clause)` | 1 |
| `0BSD` | 1 |
| `Apache-2.0` | 19 |
| `BSD-2-Clause` | 7 |
| `BSD-3-Clause` | 4 |
| `BlueOak-1.0.0` | 1 |
| `CC-BY-4.0` | 1 |
| `ISC` | 15 |
| `MIT` | 214 |
| `MPL-2.0` | 3 |
| `PostgreSQL` | 2 |

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
