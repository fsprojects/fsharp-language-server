module Foo 

let bar() = "bar"

module Nested =
    let nestedBar() = "nested!"

type Class() = 
    member this.overloadedMethod(i: int) = sprintf "%d" i
    member this.overloadedMethod(i: string) = i