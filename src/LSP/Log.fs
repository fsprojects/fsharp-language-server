module LSP.Log 

let diagnosticsLog = ref stderr

/// Print to LSP.Log.diagnosticsLog, which is stderr by default but can be redirected
let dprintfn(fmt: Printf.TextWriterFormat<'T>): 'T = 
    Printf.fprintfn diagnosticsLog.Value fmt
let dprintInfofn(fmt: Printf.TextWriterFormat<'T>): 'T = 
    Printf.fprintfn diagnosticsLog.Value fmt
let dprintVerbosefn(fmt: Printf.TextWriterFormat<'T>): 'T = 
    Printf.fprintfn diagnosticsLog.Value fmt