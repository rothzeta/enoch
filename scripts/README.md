# Repository automation

Keep reusable automation here and expose its supported operations through the
root `justfile`. Scripts fail on errors, preserve exit codes, resolve paths from
the repository root, and never contain credentials.

`run-tool.sh` runs .NET and Node tools locally by default. Set
`ENOCH_TOOLING=docker` to run them in the declared .NET 8 and Node.js 22 images.
The Docker mode writes build output as the invoking user and keeps dependency
caches under the ignored `.cache/tooling/` directory.

`check-renovate.sh` runs the pinned official Renovate configuration validator in
a separate Node 24 Docker image through `just renovate-check`. Its npm cache is
ignored under `.cache/renovate/`; it mounts repository source read-only.

`run-backup.mjs` implements verified quiesced backup/restore; `backup.test.mjs`
retains integrity and rejected-input regressions. `run-operations-check.sh` and
`check-operations.mjs` exercise an isolated Docker image and restored API fixture.
`run-local-docker.sh` leaves a checked local instance running on a loopback port.
Invoke these through `just`; [operator guidance](../docs/exploitation/backup-and-restore.md)
describes ownership, paths, credentials and limits.
