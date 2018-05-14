module Log 

open System
open Printf

// Print to stderr with timing information, to help us 
let log (fmt: TextWriterFormat<'T>): 'T = 
    eprintf "%s " (DateTime.Now.ToString("ss.fff"))
    eprintfn fmt