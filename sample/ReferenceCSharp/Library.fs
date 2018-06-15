namespace ReferenceCSharp

module Say =
    let hello () =
        let csharp = CSharpProject.Class1.name()
        printfn "Hello %s" csharp
