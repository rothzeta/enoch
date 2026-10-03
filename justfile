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

# Run all required checks; dependencies must already be restored.
check: format-check build
    ./bin/enoch-tool npm --prefix src/Enoch.Ui run lint
    ./bin/enoch-tool dotnet test Enoch.sln --no-build --no-restore
    ./bin/enoch-tool npm --prefix src/Enoch.Ui test
    git diff --check
