#!/usr/bin/env bash
# Build the test projects and run all tests

echo 'Building sample projects...'
dotnet restore sample/MainProject/MainProject.fsproj
dotnet restore sample/HasLocalDll/HasLocalDll.fsproj
dotnet restore sample/FSharpKoans.Core/FSharpKoans.Core.fsproj

echo 'Running tests...'
set -e
dotnet test tests/LSP.Tests
dotnet test tests/ProjectCracker.Tests
dotnet test tests/FSharpLanguageServer.Tests