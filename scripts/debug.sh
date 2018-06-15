
#!/usr/bin/env bash
set -e

# Builds src/FSharpLanguageServer/bin/Release/netcoreapp2.0/osx.10.11-x64/publish/FSharpLanguageServer
dotnet publish -c Release -r osx.10.11-x64 src/FSharpLanguageServer
echo 'Press F5 to debug the new build of F# language server'