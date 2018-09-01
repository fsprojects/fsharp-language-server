module HasLocalDll

[<EntryPoint>]
let main(argv) = 
    printf "Hello, %d" IndirectLibrary.myInt
    0