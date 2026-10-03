#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

if [[ $# -eq 0 ]]; then
  echo 'Usage: bin/enoch-tool dotnet|npm|node [arguments...]' >&2
  exit 2
fi

tool="$1"
shift
case "$tool" in
  dotnet) image='mcr.microsoft.com/dotnet/sdk:8.0' ;;
  npm|node) image='node:22-alpine' ;;
  *) echo "Unsupported tool: $tool" >&2; exit 2 ;;
esac

case "${ENOCH_TOOLING:-local}" in
  local) exec "$tool" "$@" ;;
  docker)
    mkdir -p .cache/tooling
    exec docker run --rm \
      --user "$(id -u):$(id -g)" \
      --volume "$repo_root:/workspace" \
      --volume "$repo_root/.cache/tooling:/tooling" \
      --workdir /workspace \
      --env HOME=/tooling \
      --env DOTNET_CLI_HOME=/tooling \
      --env DOTNET_NOLOGO=1 \
      --env DOTNET_CLI_TELEMETRY_OPTOUT=1 \
      --env NUGET_PACKAGES=/tooling/nuget \
      --env npm_config_cache=/tooling/npm \
      "$image" "$tool" "$@"
    ;;
  *) echo 'ENOCH_TOOLING must be local or docker.' >&2; exit 2 ;;
esac
