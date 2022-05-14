module ProjectCrackerMain

open System
open System.IO
open System.Collections.Generic
open ProjInfoCracker
[<EntryPoint>]
let main(argv: array<string>): int = 
    let r=crackProj(Path.GetFullPath"C:\\Users\\Eli\\Documents\\programming\\FSharp\\testProj\\testProj.fsproj")
    
    1