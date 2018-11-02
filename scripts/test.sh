#!/usr/bin/env bash
# Build the test projects and run all tests

echo 'Building sample projects...'
dotnet restore sample/EmptyProject/EmptyProject.fsproj
dotnet restore sample/MainProject/MainProject.fsproj
dotnet restore sample/HasLocalDll/HasLocalDll.fsproj
dotnet restore sample/FSharpKoans.Core/FSharpKoans.Core.fsproj
dotnet restore sample/HasTests/HasTests.fsproj
dotnet restore sample/ReferenceCSharp/ReferenceCSharp.fsproj
dotnet restore sample/ReferenceCSharp.AssemblyName/ReferenceCSharp.AssemblyName.fsproj
dotnet restore sample/Signature/Signature.fsproj
dotnet restore sample/HasPackageReference/HasPackageReference.fsproj
dotnet build sample/CSharpProject/CSharpProject.csproj
dotnet build sample/CSharpProject.AssemblyName/CSharpProject.AssemblyName.csproj
dotnet build sample/SlnReferences/ReferencedProject.fsproj
# Be sure to update .circleci/config.yml when you add sample projects

echo 'Running tests...'
set -e
dotnet test tests/LSP.Tests
dotnet test tests/ProjectCracker.Tests
dotnet test tests/FSharpLanguageServer.Tests