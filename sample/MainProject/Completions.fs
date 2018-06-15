module Completions

let private completeListModule() = 
    List.

let private completeParens() = 
    Some("foo")

let private ``name with space``() = 
    ""

let private completeSpace() = 
    na