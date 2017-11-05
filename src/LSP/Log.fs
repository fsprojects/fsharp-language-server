module LSP.Log 

open System

let consoleWriteLine (message: string) = 
    Console.Error.Write message 
    Console.Error.WriteLine()
    ()
let info (format: Printf.StringFormat<'Then, unit>): 'Then = 
    Printf.kprintf consoleWriteLine format