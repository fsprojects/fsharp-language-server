module LSP.JsonTests

open System
open System.Text.RegularExpressions
open FSharp.Data
open LSP.Json.Ser
open Expecto

LSP.Log.diagnosticsLog := stdout

let removeSpace(expected: string) = 
    Regex.Replace(expected, @"\s", "")

let tests=
    testList "Json Tests" [
        test "remove space from string"{
            let found = removeSpace("foo bar")
            Expect.equal "foobar" found "fail"
        }
        
        test "remove newline from string"{
            let actual = """foo 
            bar"""
            let found = removeSpace(actual)
            Expect.equal "foobar" found "fail"
        }
        
        test "serialize primitive types to JSON"{
            let found = serializerFactory<bool> defaultJsonWriteOptions true
            Expect.equal "true" found "fail"
            let found = serializerFactory<int> defaultJsonWriteOptions 1
            Expect.equal "1" found "fail"
            let found = serializerFactory<string> defaultJsonWriteOptions "foo"
            Expect.equal "\"foo\"" found "fail"
            let found = serializerFactory<char> defaultJsonWriteOptions 'f'
            Expect.equal "\"f\"" found "fail"
        }
        
        test "serialize URI to JSON"{
            let example = Uri("https://google.com")
            let found = serializerFactory<Uri> defaultJsonWriteOptions example
            Expect.equal "\"https://google.com/\"" found "fail"
        }
        
        test "serialize JsonValue to JSON"{
            let example = JsonValue.Parse "{}"
            let found = serializerFactory<JsonValue> defaultJsonWriteOptions example
            Expect.equal "{}" found "fail"
        }
        
        test "serialize option to JSON"{
            let found = serializerFactory<int option> defaultJsonWriteOptions (Some 1)
            Expect.equal "1" found "fail"
            let found = serializerFactory<int option> defaultJsonWriteOptions (None)
            Expect.equal "null" found "fail"
        }
    ]

type SimpleRecord = {simpleMember: int}
[<Tests>]
let tests2=
    testList "Json Tests2" [
        test "serialize record to JSON"{
            let record = {simpleMember = 1}
            let found = serializerFactory<SimpleRecord> defaultJsonWriteOptions record
            Expect.equal """{"simpleMember":1}""" found "fail"
        }
        
        test "serialize list of ints to JSON"{
            let example = [1; 2]
            let found = serializerFactory<int list> defaultJsonWriteOptions example
            Expect.equal """[1,2]""" found "fail"
        }
        
        test "serialize list of strings to JSON"{
            let example = ["foo"; "bar"]
            let found = serializerFactory<string list> defaultJsonWriteOptions example
            Expect.equal """["foo","bar"]""" found "fail"
        }
        
        test "serialize a record with a custom writer"{
            let record = {simpleMember = 1}
            let customWriter(r: SimpleRecord): string = sprintf "simpleMember=%d" r.simpleMember
            let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
            let found = serializerFactory<SimpleRecord> options record
            Expect.equal "\"simpleMember=1\"" found "fail"
        }
    ]
type Foo = Bar | Doh 
type FooRecord = {foo: Foo}
[<Tests>]
let tests3  =        
    testList "Json Tests3" [
        test "serialize a union with a custom writer"{
            let record = {foo = Bar}
            let customWriter = function 
            | Bar -> 10
            | Doh -> 20
            let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
            let found = serializerFactory<FooRecord> options record
            Expect.equal """{"foo":10}""" found "fail"
        }
    ]
// type UnionWithFields =
// | OptionA of A: string 
// | OptionB of int 

// 
// let ``serialize union with fields`` () = 
//     let options = defaultJsonReadOptions
//     let serializer = serializerFactory<UnionWithFields>
//     let found = serializer options (OptionA "foo")
//     Assert.AreEqual("""{"A":"foo"}""", found)
//     let serializer = serializerFactory<UnionWithFields>
//     let found = serializer options (OptionB 1)
//     Assert.AreEqual("""{"A":[1]}""", found)

type IFoo =
    abstract member Foo: unit -> string 
type MyFoo() = 
    interface IFoo with 
        member this.Foo() = "foo"

type SimpleTypes = {
    b: bool 
    i: int 
    c: char 
    s: string 
    webUri: Uri  
    fileUri: Uri 
}
type NestedField = {
    oneField: int
}

type ComplexTypes = {
    nested: NestedField
    intList: int list 
    stringAsInt: int 
    intOptionPresent: int option 
    intOptionAbsent: int option 
}
type TestOptionalRead = {
    optionField: int option
}
type TestEnum = One | Two

let deserializeTestEnum(i: int) = 
    match i with 
    | 1 -> One  
    | 2 -> Two

type ContainsEnum = {
    e: TestEnum
}
[<Tests>]
let tests4  =        
    testList "Json Tests4" [
    test "serialize an interface with a custom writer"{
        let customWriter(foo: IFoo): string = 
            foo.Foo()
        let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
        let example = MyFoo()
        let found = serializerFactory<IFoo> options example
        Expect.equal "\"foo\"" found "fail"
        let found = serializerFactory<MyFoo> options example
        Expect.equal "\"foo\"" found "fail"
    }

    
    test "deserialize simple types"{
        let sample = """
        {
            "b": true,
            "i": 1,
            "c": "x",
            "s": "foo",
            "webUri": "https://github.com",
            "fileUri": "file:///d%3A/foo.txt"
        }"""
        let options = defaultJsonReadOptions
        let found = deserializerFactory<SimpleTypes> options (JsonValue.Parse sample)
        Expect.equal true found.b "fail"
        Expect.equal 1 found.i "fail"
        Expect.equal 'x' found.c "fail"
        Expect.equal "foo" found.s "fail"
        Expect.equal (Uri("https://github.com")) found.webUri "fail"
        Expect.equal "d:\\foo.txt" found.fileUri.LocalPath "fail"
    }

    
    test "deserialize complex types"{
        let sample = """
        {
            "nested": {
                "oneField": 1
            },
            "intList": [1],
            "stringAsInt": "1",
            "intOptionPresent": 1,
            "intOptionAbsent": null
        }"""
        let options = defaultJsonReadOptions
        let found = deserializerFactory<ComplexTypes> options (JsonValue.Parse sample)
        Expect.equal {oneField=1} found.nested "fail"
        Expect.equal 1 found.stringAsInt "fail"
        Expect.equal [1] found.intList "fail"
        Expect.equal (Some 1) found.intOptionPresent "fail"
        Expect.equal None found.intOptionAbsent "fail"
    }

    
    test "deserialize optional types"{
        let options = defaultJsonReadOptions
        let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":1}""")
        Expect.equal {optionField=Some 1} found "fail"
        let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":null}""")
        Expect.equal {optionField=None} found "fail"
        let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{}""")
        Expect.equal {optionField=None} found "fail"
        let found = deserializerFactory<int option list> options (JsonValue.Parse """[1]""")
        Expect.equal [Some 1] found "fail"
        let found = deserializerFactory<int option list> options (JsonValue.Parse """[null]""")
        let noneIntList: int option list=[None]
        Expect.equal noneIntList found "fail"
    }
    
    test "deserialize map"{
        let options = defaultJsonReadOptions
        let found = deserializerFactory<Map<string, int>> options (JsonValue.Parse """{"k":1}""")
        let map = Map.add "k" 1 Map.empty
        Expect.equal map found "fail"
    }
    
    test "deserialize enum"{
        let options = { defaultJsonReadOptions with customReaders = [deserializeTestEnum]}
        let found = deserializerFactory<ContainsEnum> options (JsonValue.Parse """{"e":1}""")
        Expect.equal One found.e "fail"
    }
]