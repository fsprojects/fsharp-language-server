$plat = "win10-x64","linux-x64","osx.10.11-x64"

New-Item -ItemType Directory -Force -Name out
Remove-Item out/* -Recurse -Force
New-Item -ItemType Directory -Force out/server

npm install
npm run compile

foreach($i in $plat) {
    dotnet publish -f netcoreapp2.2 -c Release --self-contained `
        -r $i src/FSharpLanguageServer -o ./out/server/$i
}

npm pack
