#!/usr/bin/env bash
# Restore test projects

echo 'Restoring sample projects...'
dotnet tool restore
dotnet restore sample/EmptyProject/EmptyProject.fsproj
dotnet restore sample/FSharpKoans.Core/FSharpKoans.Core.fsproj
dotnet restore sample/HasLocalDll/HasLocalDll.fsproj
dotnet restore sample/HasPackageReference/HasPackageReference.fsproj
dotnet restore sample/HasTests/HasTests.fsproj
dotnet restore sample/Issue28/Issue28.fsproj
dotnet restore sample/MainProject/MainProject.fsproj
dotnet restore sample/ReferenceCSharp.AssemblyName/ReferenceCSharp.AssemblyName.fsproj
dotnet restore sample/ReferenceCSharp/ReferenceCSharp.fsproj
dotnet restore sample/Signature/Signature.fsproj
dotnet restore sample/SlnReferences/ReferencedProject.fsproj
dotnet restore sample/TemplateParams/TemplateParams.fsproj
dotnet restore sample/NetCoreApp3/NetCoreApp3.fsproj
dotnet restore sample/Net6Windows/Net6Windows.fsproj
# These need to be built, not restored
dotnet build sample/CSharpProject/CSharpProject.csproj
dotnet build sample/CSharpProject.AssemblyName/CSharpProject.AssemblyName.csproj
