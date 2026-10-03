set shell := ["bash", "-euo", "pipefail", "-c"]

# List repository commands.
default:
    @just --list

# Restore pinned dependencies.
install:
    ./bin/enoch-tool dotnet restore Enoch.sln
    ./bin/enoch-tool npm --prefix src/Enoch.Ui ci

# Apply C# and repository text formatting.
format:
    ./bin/enoch-tool dotnet format Enoch.sln --no-restore --severity warn
    ./bin/enoch-tool node src/Enoch.Ui/node_modules/prettier/bin/prettier.cjs --write .

# Verify formatting without changing files.
format-check:
    ./bin/enoch-tool dotnet format Enoch.sln --no-restore --severity warn --verify-no-changes
    ./bin/enoch-tool node src/Enoch.Ui/node_modules/prettier/bin/prettier.cjs --check .

# Enforce .NET analyzers and frontend lint rules.
lint:
    ./bin/enoch-tool dotnet build Enoch.sln --no-restore
    ./bin/enoch-tool npm --prefix src/Enoch.Ui run lint

# Check Vue scripts and templates.
typecheck:
    ./bin/enoch-tool npm --prefix src/Enoch.Ui run typecheck

# Build every .NET project and the production browser bundle.
build:
    ./bin/enoch-tool dotnet build Enoch.sln --no-restore
    ./bin/enoch-tool npm --prefix src/Enoch.Ui run build

# Run both application test suites.
test:
    ./bin/enoch-tool dotnet test Enoch.sln --no-restore
    ./bin/enoch-tool npm --prefix src/Enoch.Ui test
    ./bin/enoch-operations test

# Run all required checks; dependencies must already be restored.
check: format-check build
    ./bin/enoch-tool npm --prefix src/Enoch.Ui run lint
    ./bin/enoch-tool dotnet test Enoch.sln --no-build --no-restore
    ./bin/enoch-tool npm --prefix src/Enoch.Ui test
    ./bin/enoch-operations test
    git diff --check

# Back up stopped storage; supply --quiesced only after every writer is stopped.
backup data_root backup_directory confirmation='':
    ./bin/enoch-operations backup {{quote(data_root)}} {{quote(backup_directory)}} {{quote(confirmation)}}

# Verify a backup and restore it into a destination that does not yet exist.
restore backup_directory restored_data_root:
    ./bin/enoch-operations restore {{quote(backup_directory)}} {{quote(restored_data_root)}}

# Exercise backup/restore integrity and rejection behavior.
operations-test:
    ./bin/enoch-operations test

# Build and exercise an isolated local production-image API and restored storage.
operations-check:
    ./bin/enoch-operations check

# Leave the verified local image running on a loopback-only Docker port.
local-launch:
    ./bin/enoch-operations launch

# Stop the exact local container while retaining its disposable data volume.
local-stop container_name:
    ./bin/enoch-operations stop {{quote(container_name)}}

# Verify compiled CLI publication families against the last checked local app.
cli-docker-check:
    ./bin/enoch-operations cli-check
