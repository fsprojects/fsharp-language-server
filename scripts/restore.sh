#!/usr/bin/env bash
# Restore test projects

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
dotnet restore sample/TemplateParams/TemplateParams.fsproj
# TODO could these be restore instead of build?
dotnet build sample/CSharpProject/CSharpProject.csproj
dotnet build sample/CSharpProject.AssemblyName/CSharpProject.AssemblyName.csproj
dotnet build sample/Issue28/Issue28.fsproj
dotnet build sample/SlnReferences/ReferencedProject.fsproj