#!/usr/bin/env bash
# Installs locally
# You will need java, maven, vsce, and visual studio code to run this script
set -e

# Needed once
npm install

# Build self-contained archives for windows and mac
dotnet clean
dotnet publish -c Release -r win10-x64 src/Main
dotnet publish -c Release -r osx.10.11-x64 src/Main

# Build vsix
vsce package -o build.vsix

echo 'Install build.vsix using the extensions menu'