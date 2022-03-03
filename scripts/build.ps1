npm install
paket install
dotnet restore
dotnet clean
dotnet publish -c Release src/FSharpLanguageServer
#dotnet publish -c Release -r osx.10.11-x64 src/FSharpLanguageServer
#dotnet publish -c Release -r linux-x64 src/FSharpLanguageServer

# Build vsix
vsce package -o build.vsix
code --install-extension build.vsix