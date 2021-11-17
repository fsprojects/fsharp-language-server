module Hover

let private myFun(): int = 1

let private testFun() = 
    eprintfn "%d" (myFun())

module private InternalHover = 
    let internalFun(): int = 1

let private testInternalFun() = 
    eprintfn "%d" (InternalHover.internalFun())

let private systemFuncHover=List.fold