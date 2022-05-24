module Hover
open System.Net
let private myFun(): int = 1

let private testFun() = 
    eprintfn "%d" (myFun())

module private InternalHover = 
    let internalFun(): int = 1

let private testInternalFun() = 
    eprintfn "%d" (InternalHover.internalFun())

let private systemFuncHover=List.fold

type intFunc= int->int
let multiply a b c =
    a*b*c
let aliasedFunc:intFunc = (multiply 1 2)
///This function has documentation 
///``a``: a thing
///``b``:  b thing
let docedFunction a b=
   a+b
let methodTest=Authorization("a")

type DUHover=
    |HoverCase of string