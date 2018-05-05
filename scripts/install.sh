#!/usr/bin/env bash
# Installs locally
# You will need java, maven, vsce, and visual studio code to run this script
set -e

# Needed once
npm install

# Build fat jar
dotnet build src/Main

# Build vsix
vsce package -o build.vsix

echo 'Install build.vsix using the extensions menu'