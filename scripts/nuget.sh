#!/usr/bin/env bash
set -e

# Build F# compiler service, which is assumed to be in a sibling directory named 'FSharp.Compiler.Service'
cd ../FSharp.Compiler.Service/
fcs/build.sh NuGet

# Copy result of previous step to nuget/, a local nuget feed
cd ../fsharp-language-server
rm -rf nuget/fsharp.compiler.service
rm -rf src/FSharpLanguageServer/obj
rm -rf src/FSharpLanguageServer/bin
rm -rf ~/.nuget/packages/fsharp.compiler.service/1000.0.0
nuget add ../FSharp.Compiler.Service/release/fcs/FSharp.Compiler.Service.1000.0.0.nupkg -Source ./nuget
dotnet restore src/FSharpLanguageServer