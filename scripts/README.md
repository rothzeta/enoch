# Repository automation

Keep reusable automation here and expose its supported operations through the
root `justfile`. Scripts fail on errors, preserve exit codes, resolve paths from
the repository root, and never contain credentials.

`run-tool.sh` runs .NET and Node tools locally by default. Set
`ENOCH_TOOLING=docker` to run them in the declared .NET 8 and Node.js 22 images.
The Docker mode writes build output as the invoking user and keeps dependency
caches under the ignored `.cache/tooling/` directory.
