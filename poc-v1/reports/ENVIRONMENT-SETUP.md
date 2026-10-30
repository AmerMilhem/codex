# Cloud environment setup outcome

The cloud-environment-onboarding:setup workflow was used for preparation and reusable configuration.
[Skill source read](skill://plugin_connector_1p_ed5feb9070a08191b08c81c47947bc16/setup/SKILL.md).

Verified in this machine: SHA512-checked .NET SDK10.0.402, locked NuGet restore, .NET build,
Node24.19.0/npm11.9.0, Angular22.2.3 production build, Chromium154, Docker28.4, PostgreSQL18.2,
46 backend checks,11 browser checks, isolated restore/reconciliation and a real local DB outage/restart.
`scripts/install.sh`, `scripts/run.sh` and `scripts/web.sh` were actually exercised. The PostgreSQL
PoC container remains available on loopback; live processes must be started again in future tasks.

Saved draft fields: `install_script`, `start_skill`, `network.allowed_domains`.
Custom domain added: `storage.googleapis.com`; existing package manager presets preserved.
[Persistence receipt](../evidence/environment-draft.json). The setup script delegates to the tested
`scripts/install.sh`; startup instructions include service checks, PoC-only scope and worktree guidance.

**Saved draft ≠ runtime change ≠ published environment.** To activate it, review and save the changes
in environment settings, then publish the environment. No publication or fresh-task restoration was performed.
Flutter's blocked archive request is still the latest runtime observation; no SDK compatibility PASS is claimed.
The PoC source is local and uncommitted in an initially empty repository. Preserve the prepared snapshot or
review/commit it separately before expecting a fresh remote clone to contain these files. No push was performed.

No external secrets were requested or saved. Synthetic local PoC credentials are never production credentials.
Managed OIDC, mobile devices/plugins and cloud-specific recovery remain separate readiness blockers.
