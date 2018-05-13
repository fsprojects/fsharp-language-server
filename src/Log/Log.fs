module Log 

open System
open Printf

let log (fmt: TextWriterFormat<'T>): 'T = 
    eprintf "%s " (DateTime.Now.ToString("ss.fff"))
    eprintfn fmt