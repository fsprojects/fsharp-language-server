#!/usr/bin/env bash
dotnet build sample/MainProject/MainProject.fsproj
dotnet run -p tests/LSP.Tests
dotnet run -p tests/Main.Tests

# To run a single test: dotnet run -p tests/Main.Tests -- "write the name of the test function here"