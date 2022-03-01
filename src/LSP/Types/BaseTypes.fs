
[<AutoOpen>]
module LSP.BaseTypes
open System
type Position = {
    line: int
    character: int
}

type Range = {
    start: Position
    ``end``: Position
}
type WorkspaceFolder = {
    uri: Uri
    name: string
}
type TextDocumentIdentifier = {
    uri: Uri
}
