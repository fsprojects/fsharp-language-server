module FSharpLanguageServer.ProjectManager.FileCache
open LSP.Log
open LSP.Utils
open System
open System.IO
open System.Collections.Generic
open FSharp.Compiler.Text
open Thoth.Json.Net
open Newtonsoft.Json
open Types

type CacheData={
    ///hash of the projects assets.json. This is used to see if the project has changed which would invalidate our hash.
    assetsHash :string
    fsprojHash :string
    Project:ResolvedProject
    ///Used to allow deleting of old cache data if we make significant changes
    version:string
}
let currentVersion="0.1.80"
type FileInfoConverter() =
    inherit JsonConverter<FileInfo>()
        override x.WriteJson( writer:JsonWriter,  value:FileInfo,  serializer:JsonSerializer)=
            writer.WriteValue(value.FullName);
        
        override x.ReadJson( reader:JsonReader,  objectType:Type,  existingValue:FileInfo,  hasExistingValue:bool,  serializer:JsonSerializer)=
            let s = reader.Value:?>string
            normedFileInfo(s)
    
let private serialzeProjectData (projectCache: Dictionary<String,LazyProject>)=
    let projectData=projectCache|> Seq.map(fun x->x.Key,x.Value.resolved.Value);
    System.Text.Json.JsonSerializer.Serialize(projectData)

let private deserializeProjectData(json:string) :Dictionary<String,LazyProject> =
    let projects=System.Text.Json.JsonSerializer.Deserialize<(string*ResolvedProject) list>(json)
    projects|>List.map(fun( name,proj)->KeyValuePair(name,{file=normedFileInfo(name); resolved= lazy(proj) }))|>Dictionary

let private getCachePath (projectPath:string)=Path.Combine(Path.GetDirectoryName(projectPath),"obj","fslspCache.json")

let getHash fileName=
    use md5 = System.Security.Cryptography.MD5.Create()
    use stream = File.OpenRead(fileName)
    md5.ComputeHash(stream) |>BitConverter.ToString

let settings = 
    JsonSerializerSettings(
        MissingMemberHandling=MissingMemberHandling.Error,
        ConstructorHandling=ConstructorHandling.AllowNonPublicDefaultConstructor,
        Converters=[|new FileInfoConverter()|]
        )

let extraEncoders=
    Extra.empty
    |>(Extra.withCustom 
        (fun (x:FileInfo)->Encode.string x.FullName)
        (fun path value->Ok (normedFileInfo(value.ToString()))))
    |>Extra.withCustom
        (fun (x:Range)->Encode.string <|System.Text.Json.JsonSerializer.Serialize(x))
        (fun path value->Ok (System.Text.Json.JsonSerializer.Deserialize<Range>(value.ToString()) ))

///Uses various methods to decide if the cache is still valid or if it needs to be discarded and replaced.
let isCacheValid (fsprojPath:string) (cachePath:string) (cacheData:CacheData)=

    let assetsPath=Path.Combine(Path.GetDirectoryName(cachePath),"project.assets.json")
    let assetHash= getHash assetsPath
    let fsprojHash= getHash fsprojPath
    cacheData.assetsHash=assetHash && cacheData.fsprojHash=fsprojHash && cacheData.version=currentVersion

///**Attempts to get cached project data.**
///
///O returns the data if the project.assets.json files hash has not changed. A change would indicate that the cached data may no longer be valid.
let tryGetCached (fsproj:FileInfo)=
    let cacheFilePath=getCachePath fsproj.FullName
    if File.Exists(cacheFilePath)then
        let cacheJson= File.ReadAllText(cacheFilePath)

        try
            let existingCacheData=match(Decode.Auto.fromString(cacheJson,extra=extraEncoders))with|Ok a->a|Error e->failwithf "error %A"e
            
            if isCacheValid fsproj.FullName cacheFilePath existingCacheData  then Ok existingCacheData 
            else 
                File.Delete(cacheFilePath)
                lgInfo "Not using cached projOptions for '{proj}' because the project.assets.json hash has changed" fsproj.FullName
                Error "Hash had changed"
        with 
        |e->
            lgWarn2 "Cached projectOptions for '{proj}' could not be read, deleting it \nReson {exception}" fsproj e
            File.Delete(cacheFilePath)
            Error(sprintf "%A" e)
    else Error "no cache file exists"
    
///Saves the project data into a cahce file so it can be loaded later 
let saveCache (projectData:ResolvedProject) (fsproj:FileInfo) =
    let cachePath=getCachePath fsproj.FullName 
    let assetsPath=Path.Combine(Path.GetDirectoryName(cachePath),"project.assets.json")
    let assetHash=getHash assetsPath
    let fsprojHash=getHash fsproj.FullName
    
    let data={assetsHash=assetHash; fsprojHash=fsprojHash;Project=projectData;version=currentVersion}
    let cacheJson= Encode.Auto.toString(4,data,extra=extraEncoders)
    File.WriteAllText(cachePath,cacheJson)
    lgInfo "Saved cache of projectOptions for '{proj}' " fsproj.FullName

let deleteCache  (fsproj:FileInfo) =
    let cachePath=getCachePath fsproj.FullName 
    try
        File.Delete(cachePath)
        lgInfo "Deleted cache for '{proj}' " fsproj.FullName
    with e-> lgWarn2 "Attempted to delete cache for {proj} but had exception {ex}" fsproj.FullName e
