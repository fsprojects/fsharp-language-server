#!/usr/bin/env bash
set -e

# Delete build outputs
rm -rf src/*/bin src/*/obj tests/*/bin tests/*/obj node_modules

# Build js
npm install
npm run-script compile

# Build src/FSharpLanguageServer/bin/Release/netcoreapp3.0/osx.10.11-x64/publish/FSharpLanguageServer
dotnet publish -c Release -r osx.10.11-x64 src/FSharpLanguageServer
echo 'Press F5 to debug the new build of F# language server'
