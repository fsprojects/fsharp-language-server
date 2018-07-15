#!/usr/bin/env bash
# Builds the plugin as build.vsix
# You will need dotnet core and vsce to run this script
set -e

# Download npm dependencies
if [ ! -d node_modules ]; then 
    npm install
fi

# Build F# compiler service
if [ ! -d nuget/fsharp.compiler.service ]; then 
    ./scripts/fcs.sh
fi

# Build self-contained archives for windows and mac
dotnet publish -c Release -r win10-x64 src/FSharpLanguageServer
dotnet publish -c Release -r osx.10.11-x64 src/FSharpLanguageServer

# Build vsix
vsce package -o build.vsix

echo 'Install build.vsix using the extensions menu'