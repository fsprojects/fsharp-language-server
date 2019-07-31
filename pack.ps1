New-Item -ItemType Directory -Force -Name out
New-Item -ItemType Directory -Force -Name publish
New-Item -ItemType Directory -Force -Name bin
Remove-Item bin/* -Recurse -Force
Remove-Item out/* -Recurse -Force
Remove-Item publish/* -Recurse -Force

# client

npm install
npm run compile
npm pack
Move-Item *.tgz publish/

# server

$plat = "win10-x64","linux-x64","osx.10.11-x64"

foreach ($i in $plat) {
    dotnet publish -f netcoreapp2.2 -c Release --self-contained `
        -r $i src/FSharpLanguageServer -o ./bin/$i
    Compress-Archive -Path ./bin/$i/* -DestinationPath publish/coc-fsharp-$i.zip -Force
}

