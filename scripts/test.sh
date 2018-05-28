#!/usr/bin/env bash
echo 'Building sample projects, there will be errors...'
dotnet build sample/MainProject/MainProject.fsproj
dotnet build sample/HasLocalDll/HasLocalDll.fsproj

echo 'Running tests...'
set -e
dotnet test tests/LSP.Tests
dotnet test tests/Projects.Tests
dotnet test tests/FSharpLanguageServer.Tests