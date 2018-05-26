
#!/usr/bin/env bash
# Builds src/Main/bin/Release/netcoreapp2.0/osx.10.11-x64/publish/Main
dotnet build src/Main
dotnet publish -c Release -r osx.10.11-x64 src/Main
echo 'Press F5 to debug the new build of F# language server'