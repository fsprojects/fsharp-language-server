module Foo

val bar: unit -> string

module Nested = 
    val nestedBar: unit -> string
    val missingImplementation: unit -> string

[<Class>]
type Class = 
    member overloadedMethod: int -> string
    member overloadedMethod: string -> string