#!/usr/bin/env bash
# Builds the plugin as build.vsix
# You will need dotnet core and vsce to run this script
set -e

# Needed once
npm install

# Build self-contained archives for windows, mac and linux
dotnet clean
dotnet publish -c Release src/FSharpLanguageServer

# Build vsix
vsce package -o build.vsix
code --install-extension build.vsix
