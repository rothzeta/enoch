#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$repo_root/.cache/renovate"

# Renovate's validator needs Node 24; the application's runtime remains Node 22.
exec docker run --rm \
  --user "$(id -u):$(id -g)" \
  --volume "$repo_root:/workspace:ro" \
  --volume "$repo_root/.cache/renovate:/tooling" \
  --workdir /workspace \
  --env npm_config_cache=/tooling/npm \
  node:24-alpine npm exec --yes --package=renovate@44.149.2 -- \
  renovate-config-validator --strict renovate.json
