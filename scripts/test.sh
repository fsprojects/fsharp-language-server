#!/usr/bin/env bash
# Restore test projects and run all tests

source scripts/restore.sh

echo 'Running tests...'
set -e
dotnet test tests/LSP.Tests
dotnet test tests/ProjectCracker.Tests
dotnet test tests/FSharpLanguageServer.Tests