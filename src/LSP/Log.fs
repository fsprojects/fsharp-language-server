module LSP.Log 

open System

let consoleWriteLine (message: string) = 
    Console.Write message 
    Console.WriteLine()
let info (format: Printf.StringFormat<'Then, unit>): 'Then = 
    Printf.kprintf consoleWriteLine format