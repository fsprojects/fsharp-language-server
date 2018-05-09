module LSP.JsonTests

open System
open System.Text.RegularExpressions
open FSharp.Data
open Json
open SimpleTest

let removeSpace (expected: string) = 
    Regex.Replace(expected, @"\s", "")

let ``test remove space from string`` (t: TestContext) = 
    let found = removeSpace "foo bar"
    if found <> "foobar" then Fail(found)

let ``test remove newline from string`` (t: TestContext) = 
    let actual = """foo 
    bar"""
    let found = removeSpace actual
    if found <> "foobar" then Fail(found)

let ``test serialize primitive types to JSON`` (t: TestContext) = 
    let found = serializerFactory<bool> defaultJsonWriteOptions true
    if found <> "true" then Fail(found)
    let found = serializerFactory<int> defaultJsonWriteOptions 1
    if found <> "1" then Fail(found)
    let found = serializerFactory<string> defaultJsonWriteOptions "foo"
    if found <> "\"foo\"" then Fail(found)
    let found = serializerFactory<char> defaultJsonWriteOptions 'f'
    if found <> "\"f\"" then Fail(found)

let ``test serialize URI to JSON`` (t: TestContext) = 
    let example = Uri("https://google.com")
    let found = serializerFactory<Uri> defaultJsonWriteOptions example
    if found <> "\"https://google.com/\"" then Fail(found)

let ``test serialize JsonValue to JSON`` (t: TestContext) = 
    let example = JsonValue.Parse "{}"
    let found = serializerFactory<JsonValue> defaultJsonWriteOptions example
    if found <> "{}" then Fail(found)

let ``test serialize option to JSON`` (t: TestContext) = 
    let found = serializerFactory<int option> defaultJsonWriteOptions (Some 1)
    if found <> "1" then Fail(found)
    let found = serializerFactory<int option> defaultJsonWriteOptions (None)
    if found <> "null" then Fail(found)

type SimpleRecord = {simpleMember: int}

let ``test serialize record to JSON`` (t: TestContext) = 
    let record = {simpleMember = 1}
    let found = serializerFactory<SimpleRecord> defaultJsonWriteOptions record
    if found <> """{"simpleMember":1}""" then Fail(found)

let ``test serialize list of ints to JSON`` (t: TestContext) = 
    let example = [1; 2]
    let found = serializerFactory<int list> defaultJsonWriteOptions example
    if found <> """[1,2]""" then Fail(found)

let ``test serialize list of strings to JSON`` (t: TestContext) = 
    let example = ["foo"; "bar"]
    let found = serializerFactory<string list> defaultJsonWriteOptions example
    if found <> """["foo","bar"]""" then Fail(found)

let ``test serialize a record with a custom writer`` (t: TestContext) = 
    let record = {simpleMember = 1}
    let customWriter (r: SimpleRecord): string = sprintf "simpleMember=%d" r.simpleMember
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let found = serializerFactory<SimpleRecord> options record
    if found <> "\"simpleMember=1\"" then Fail(found)

type Foo = Bar | Doh 
type FooRecord = {foo: Foo}

let ``test serialize a union with a custom writer`` (t: TestContext) = 
    let record = {foo = Bar}
    let customWriter = function 
    | Bar -> 10
    | Doh -> 20
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let found = serializerFactory<FooRecord> options record
    if found <> """{"foo":10}""" then Fail(found)

type IFoo =
    abstract member Foo: unit -> string 
type MyFoo() = 
    interface IFoo with 
        member this.Foo() = "foo"

let ``test serialize an interface with a custom writer`` (t: TestContext) = 
    let customWriter (foo: IFoo): string = 
        foo.Foo()
    let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
    let example = MyFoo()
    let found = serializerFactory<IFoo> options example
    if found <> "\"foo\"" then Fail(found)
    let found = serializerFactory<MyFoo> options example
    if found <> "\"foo\"" then Fail(found)

let ``test deserialize simple types`` (t: TestContext) = 
    let options = defaultJsonReadOptions
    let found = deserializerFactory<bool> options (JsonValue.Parse "true")
    if found <> true then Fail(found)
    let found = deserializerFactory<int> options (JsonValue.Parse "1")
    if found <> 1 then Fail(found)
    let found = deserializerFactory<char> options (JsonValue.Parse "\"x\"")
    if found <> 'x' then Fail(found)
    let found = deserializerFactory<string> options (JsonValue.Parse "\"foo\"")
    if found <> "foo" then Fail(found)
    let found = deserializerFactory<Uri> options (JsonValue.Parse "\"https://github.com\"")
    if found <> Uri("https://github.com") then Fail(found)

type TestSimpleRead = {
    oneField: int
}

let ``test deserialize complex types`` (t: TestContext) = 
    let options = defaultJsonReadOptions
    let found = deserializerFactory<TestSimpleRead> options (JsonValue.Parse """{"oneField":1}""")
    if found <> {oneField=1} then Fail(found)
    let found = deserializerFactory<int list> options (JsonValue.Parse """[1]""")
    if found <> [1] then Fail(found)
    let found = deserializerFactory<int option> options (JsonValue.Parse """1""")
    if found <> Some 1 then Fail(found)
    let found = deserializerFactory<int option> options (JsonValue.Parse """null""")
    if found <> None then Fail(found)

type TestOptionalRead = {
    optionField: int option
}

let ``test deserialize optional types`` (t: TestContext) = 
    let options = defaultJsonReadOptions
    let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":1}""")
    if found <> {optionField=Some 1} then Fail(found)
    let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":null}""")
    if found <> {optionField=None} then Fail(found)
    let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{}""")
    if found <> {optionField=None} then Fail(found)
    let found = deserializerFactory<int option list> options (JsonValue.Parse """[1]""")
    if found <> [Some 1] then Fail(found)
    let found = deserializerFactory<int option list> options (JsonValue.Parse """[null]""")
    if found <> [None] then Fail(found)

type TestEnum = One | Two

let deserializeTestEnum (i: int) = 
    match i with 
    | 1 -> One  
    | 2 -> Two

let ``test deserialize enum`` (t: TestContext) = 
    let options = { defaultJsonReadOptions with customReaders = [deserializeTestEnum]}
    let found = deserializerFactory<TestEnum> options (JsonValue.Parse """1""")
    if found <> One then Fail(found)