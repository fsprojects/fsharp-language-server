namespace LSP

open Parser
open NUnit.Framework

module ParserTests =
    [<Test>]
    let ``parse a simple request`` () = 
        let json = """{
            "jsonrpc": "2.0",
            "id": 1,
            "method": "helloWorld"
        }"""
        Assert.That(
            parseRequestMessage json, 
            Is.EqualTo (RequestMessage (1, "helloWorld", None)))
