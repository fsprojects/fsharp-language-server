module SimpleTest

open System
open System.Reflection
open System.Diagnostics
open System.Runtime.CompilerServices

type TestContext = 
    abstract member Placeholder: unit

exception TestFailure of message: string

let inline Fail(reason: obj): unit = 
    let caller = new StackFrame(0, true)
    let file = caller.GetFileName()
    let line = caller.GetFileLineNumber()
    let column = caller.GetFileColumnNumber()
    let message = sprintf "%s(%d,%d) %A" file line column reason
    raise(TestFailure(message))

let private isTest(m: MethodInfo) = 
    let ps = m.GetParameters()
    m.Name.StartsWith("test") && ps.Length = 1 && ps.[0].ParameterType = typeof<TestContext>

let runAllTests(assembly: Assembly): unit =
    let mutable countSucceeded, countFailed = 0, 0
    for t in assembly.GetTypes() do 
        for m in t.GetMethods() do 
            if isTest(m) then 
                let context = { new TestContext with 
                    member this.Placeholder = ()}
                try 
                    eprintfn "\u001b[33mRunning: %s.%s\u001b[0m" t.Name m.Name
                    m.Invoke(null, [|context :> obj|]) |> ignore
                    eprintfn "\u001b[32mSucceeded: %s.%s\u001b[0m" t.Name m.Name
                    countSucceeded <- countSucceeded + 1
                with 
                | :? TargetInvocationException as ex ->
                    match ex.InnerException with 
                    | TestFailure(message) -> 
                        eprintfn "\u001b[31mFailed: %s.%s" t.Name m.Name
                        eprintfn "  %s" message
                        countFailed <- countFailed + 1
                    | _ -> reraise()
    eprintfn "\u001b[33mRan: %d  \u001b[32mSucceeded: %d  \u001b[31mFailed: %d\u001b[0m" (countSucceeded + countFailed) countSucceeded countFailed