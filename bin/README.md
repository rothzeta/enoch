# Repository commands

This directory contains tracked executable entry points. `enoch-tool` delegates to
`scripts/run-tool.sh`; developers normally invoke it through the root `justfile`.

Generated .NET output belongs in each project's ignored `bin/` directory. It must
not be placed in this repository-root directory.

`enoch-operations` delegates backup, restore, operational tests and local Docker
launch/stop to `scripts/`; its supported operations are exposed through `just`.

`enoch-renovate-check` delegates configuration validation to
`scripts/check-renovate.sh` through `just renovate-check`.
