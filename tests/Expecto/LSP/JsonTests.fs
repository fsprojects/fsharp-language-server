module LSP.JsonTests

open System
open System.Text.RegularExpressions
open FSharp.Data
open LSP.Json.Ser
open NUnit.Framework
open Expecto

LSP.Log.diagnosticsLog := stdout

let removeSpace(expected: string) = 
    Regex.Replace(expected, @"\s", "")

[<Tests>]
let tests=
    testList "Json Tests" [
        test "remove space from string"{
            let found = removeSpace("foo bar")
            Assert.AreEqual("foobar", found)
        }
        
        test "remove newline from string"{
            let actual = """foo 
            bar"""
            let found = removeSpace(actual)
            Assert.AreEqual("foobar", found)
        }
        
        test "serialize primitive types to JSON"{
            let found = serializerFactory<bool> defaultJsonWriteOptions true
            Assert.AreEqual("true", found)
            let found = serializerFactory<int> defaultJsonWriteOptions 1
            Assert.AreEqual("1", found)
            let found = serializerFactory<string> defaultJsonWriteOptions "foo"
            Assert.AreEqual("\"foo\"", found)
            let found = serializerFactory<char> defaultJsonWriteOptions 'f'
            Assert.AreEqual("\"f\"", found)
        }
        
        test "serialize URI to JSON"{
            let example = Uri("https://google.com")
            let found = serializerFactory<Uri> defaultJsonWriteOptions example
            Assert.AreEqual("\"https://google.com/\"", found)
        }
        
        test "serialize JsonValue to JSON"{
            let example = JsonValue.Parse "{}"
            let found = serializerFactory<JsonValue> defaultJsonWriteOptions example
            Assert.AreEqual("{}", found)
        }
        
        test "serialize option to JSON"{
            let found = serializerFactory<int option> defaultJsonWriteOptions (Some 1)
            Assert.AreEqual("1", found)
            let found = serializerFactory<int option> defaultJsonWriteOptions (None)
            Assert.AreEqual("null", found)
        }
    ]

type SimpleRecord = {simpleMember: int}
[<Tests>]
let tests2=
    testList "Json Tests2" [
        test "serialize record to JSON"{
            let record = {simpleMember = 1}
            let found = serializerFactory<SimpleRecord> defaultJsonWriteOptions record
            Assert.AreEqual("""{"simpleMember":1}""", found)
        }
        
        test "serialize list of ints to JSON"{
            let example = [1; 2]
            let found = serializerFactory<int list> defaultJsonWriteOptions example
            Assert.AreEqual("""[1,2]""", found)
        }
        
        test "serialize list of strings to JSON"{
            let example = ["foo"; "bar"]
            let found = serializerFactory<string list> defaultJsonWriteOptions example
            Assert.AreEqual("""["foo","bar"]""", found)
        }
        
        test "serialize a record with a custom writer"{
            let record = {simpleMember = 1}
            let customWriter(r: SimpleRecord): string = sprintf "simpleMember=%d" r.simpleMember
            let options = {defaultJsonWriteOptions with customWriters = [customWriter]}
            let found = serializerFactory<SimpleRecord> options record
            Assert.AreEqual("\"simpleMember=1\"", found)
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
            Assert.AreEqual("""{"foo":10}""", found)
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
        Assert.AreEqual("\"foo\"", found)
        let found = serializerFactory<MyFoo> options example
        Assert.AreEqual("\"foo\"", found)
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
        Assert.AreEqual(true, found.b)
        Assert.AreEqual(1, found.i)
        Assert.AreEqual('x', found.c)
        Assert.AreEqual("foo", found.s)
        Assert.AreEqual(Uri("https://github.com"), found.webUri)
        Assert.AreEqual("d:\\foo.txt", found.fileUri.LocalPath)
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
        Assert.AreEqual({oneField=1}, found.nested)
        Assert.AreEqual(1, found.stringAsInt)
        Assert.AreEqual([1], found.intList)
        Assert.AreEqual(Some 1, found.intOptionPresent)
        Assert.AreEqual(None, found.intOptionAbsent)
    }

    
    test "deserialize optional types"{
        let options = defaultJsonReadOptions
        let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":1}""")
        Assert.AreEqual({optionField=Some 1}, found)
        let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{"optionField":null}""")
        Assert.AreEqual({optionField=None}, found)
        let found = deserializerFactory<TestOptionalRead> options (JsonValue.Parse """{}""")
        Assert.AreEqual({optionField=None}, found)
        let found = deserializerFactory<int option list> options (JsonValue.Parse """[1]""")
        Assert.AreEqual([Some 1], found)
        let found = deserializerFactory<int option list> options (JsonValue.Parse """[null]""")
        let noneIntList: int option list=[None]
        Assert.AreEqual(noneIntList, found)
    }
    
    test "deserialize map"{
        let options = defaultJsonReadOptions
        let found = deserializerFactory<Map<string, int>> options (JsonValue.Parse """{"k":1}""")
        let map = Map.add "k" 1 Map.empty
        Assert.AreEqual(map, found)
    }
    
    test "deserialize enum"{
        let options = { defaultJsonReadOptions with customReaders = [deserializeTestEnum]}
        let found = deserializerFactory<ContainsEnum> options (JsonValue.Parse """{"e":1}""")
        Assert.AreEqual(One, found.e)
    }
]