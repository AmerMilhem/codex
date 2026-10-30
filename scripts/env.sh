#!/usr/bin/env bash
# Source this file; never prints connection strings or tokens.
if [[ -d /workspace/erp-poc-v1/tools ]]; then
  export POC_TOOLS="${POC_TOOLS:-/workspace/erp-poc-v1/tools}"
else
  erp_repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  export POC_TOOLS="${POC_TOOLS:-$erp_repo_root/.local/tools}"
fi
if [[ -x "$POC_TOOLS/dotnet/dotnet" ]]; then export PATH="$POC_TOOLS/dotnet:$PATH"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$POC_TOOLS/cli}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$POC_TOOLS/nuget}"
export npm_config_cache="${npm_config_cache:-$POC_TOOLS/npm}"
