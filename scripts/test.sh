#!/usr/bin/env bash
dotnet build sample/MainProject/MainProject.fsproj
dotnet test tests/LSP.Tests
dotnet test tests/Main.Tests

# To run a single test: dotnet run -p tests/Main.Tests -- "write the name of the test function here"