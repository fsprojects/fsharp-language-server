namespace ReferenceCSharp.AssemblyName

module Say =
    let hello () =
        let csharp = CSharpProject.AssemblyName.Class1.name()
        printfn "Hello %s" csharp
