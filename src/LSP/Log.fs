module LSP.Log 

let diagnosticsLog = ref stderr

let dprintfn(fmt: Printf.TextWriterFormat<'T>): 'T = 
    Printf.fprintfn !diagnosticsLog fmt