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

let private matchesArgs (argv: array<string>) (m: MethodInfo) = 
    Array.isEmpty argv || Array.exists (fun arg -> m.Name.Contains(arg)) argv

let runAllTests(assembly: Assembly, argv: array<string>): unit =
    if not (Array.isEmpty argv) then 
        eprintf "Looking for tests that match %A" argv
    let mutable countTests = 0
    let mutable failures: (Type * MethodInfo * string) list = []
    for t in assembly.GetTypes() do 
        for m in t.GetMethods() do 
            if isTest(m) && (matchesArgs argv m) then 
                countTests <- countTests + 1
                let context = { new TestContext with 
                                    member this.Placeholder = ()}
                try 
                    eprintfn "\u001b[33mRunning: %s.%s\u001b[0m" t.Name m.Name
                    m.Invoke(null, [|context :> obj|]) |> ignore
                    eprintfn "\u001b[32mSucceeded: %s.%s\u001b[0m" t.Name m.Name
                with 
                | :? TargetInvocationException as ex ->
                    match ex.InnerException with 
                    | TestFailure(message) -> 
                        eprintfn "\u001b[31mFailed: %s.%s" t.Name m.Name
                        eprintfn "  %s" message
                        failures <- (t, m, message)::failures
                    | _ -> reraise()
    let countFailed = List.length failures
    eprintf "\u001b[33mRan: %d  \u001b[32mSucceeded: %d " countTests (countTests - countFailed)
    if countFailed > 0 then eprintfn "  \u001b[31mFailed: %d" countFailed 
    for t, m, message in List.rev failures do 
        eprintfn "Failed: %s.%s" t.Name m.Name
        eprintfn "  %s" message
    eprintfn "\u001b[0m"