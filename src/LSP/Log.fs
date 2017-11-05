module LSP.Log 

open System

let private consoleWriteLine (message: string) = 
    Console.Error.Write message 
    Console.Error.WriteLine()
    ()
let info (format: Printf.StringFormat<'Then, unit>): 'Then = 
    Printf.kprintf consoleWriteLine format
let warn (format: Printf.StringFormat<'Then, unit>): 'Then = 
    Printf.kprintf consoleWriteLine format