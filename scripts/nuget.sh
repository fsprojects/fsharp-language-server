#!/usr/bin/env bash
set -e

# Build F# compiler service, which is assumed to be in a sibling directory named 'FSharp.Compiler.Service'
cd ../FSharp.Compiler.Service/
fcs/build.sh NuGet

# Copy result of previous step to nuget/, a local nuget feed
cd ../fsharp-language-server
rm -rf nuget/fsharp.compiler.service
nuget add ../FSharp.Compiler.Service/release/fcs/FSharp.Compiler.Service.24.0.1.nupkg -Source ./nuget