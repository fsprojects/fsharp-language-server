#!/usr/bin/env bash
echo 'Building sample projects, there will be errors...'
dotnet build sample/MainProject/MainProject.fsproj
dotnet build sample/HasLocalDll/HasLocalDll.fsproj
echo 'Running tests...'
dotnet test tests/LSP.Tests
dotnet test tests/Main.Tests

# To run a single test: dotnet run -p tests/Main.Tests -- "write the name of the test function here"