module FSharpLanguageServer.UnusedDeclarations 

// From https://github.com/Microsoft/visualfsharp/blob/master/vsintegration/src/FSharp.Editor/Diagnostics/UnusedOpensDiagnosticAnalyzer.fs

open System.Collections.Generic
open FSharp.Compiler.EditorServices
open FSharp.Compiler.Symbols
open FSharp.Compiler.CodeAnalysis

type FSharpSymbol with
    member this.IsPrivateToFile =
        match this with
        | :? FSharpMemberOrFunctionOrValue as m -> not m.IsModuleValueOrMember
        | :? FSharpEntity as m -> m.Accessibility.IsPrivate
        | :? FSharpGenericParameter -> true
        | :? FSharpUnionCase as m -> m.Accessibility.IsPrivate
        | :? FSharpField as m -> m.Accessibility.IsPrivate
        | _ -> false
    member this.IsInternalToProject =
        match this with
        | :? FSharpParameter -> true
        | :? FSharpMemberOrFunctionOrValue as m -> not m.IsModuleValueOrMember || not m.Accessibility.IsPublic
        | :? FSharpEntity as m -> not m.Accessibility.IsPublic
        | :? FSharpGenericParameter -> true
        | :? FSharpUnionCase as m -> not m.Accessibility.IsPublic
        | :? FSharpField as m -> not m.Accessibility.IsPublic
        | _ -> false

type FSharpSymbolUse with
    member this.IsPrivateToFile =
        let isPrivate =
            match this.Symbol with
            | :? FSharpMemberOrFunctionOrValue as m -> not m.IsModuleValueOrMember || m.Accessibility.IsPrivate
            | :? FSharpEntity as m -> m.Accessibility.IsPrivate
            | :? FSharpGenericParameter -> true
            | :? FSharpUnionCase as m -> m.Accessibility.IsPrivate
            | :? FSharpField as m -> m.Accessibility.IsPrivate
            | _ -> false
        let declarationLocation =
            match this.Symbol.SignatureLocation with
            | Some x -> Some x
            | _ ->
                match this.Symbol.DeclarationLocation with
                | Some x -> Some x
                | _ -> this.Symbol.ImplementationLocation
        let declaredInTheFile =
            match declarationLocation with
            | Some declRange -> declRange.FileName = this.Range.FileName
            | _ -> false
        isPrivate && declaredInTheFile

let private isPotentiallyUnusedDeclaration(symbol: FSharpSymbol) : bool =
    match symbol with
    // Determining that a record, DU or module is used anywhere requires inspecting all their enclosed entities (fields, cases and func / vals)
    // for usages, which is too expensive to do. Hence we never gray them out.
    | :? FSharpEntity as e when e.IsFSharpRecord || e.IsFSharpUnion || e.IsInterface || e.IsFSharpModule || e.IsClass || e.IsNamespace -> false
    // FCS returns inconsistent results for override members; we're skipping these symbols.
    | :? FSharpMemberOrFunctionOrValue as f when 
            f.IsOverrideOrExplicitInterfaceImplementation ||
            f.IsBaseValue ||
            f.IsConstructor -> false
    // Usage of DU case parameters does not give any meaningful feedback; we never gray them out.
    | :? FSharpParameter -> false
    | _ -> true

let getUnusedDeclarationRanges(symbolsUses: FSharpSymbolUse[], isScript: bool) =
    let definitions =
        symbolsUses
        |> Array.filter (fun su -> 
            su.IsFromDefinition && 
            su.Symbol.DeclarationLocation.IsSome && 
            (isScript || su.IsPrivateToFile) && 
            not (su.Symbol.DisplayName.StartsWith "_") &&
            isPotentiallyUnusedDeclaration su.Symbol)
    let usages =
        let usages = 
            symbolsUses
            |> Array.filter (fun su -> not su.IsFromDefinition)
            |> Array.choose (fun su -> su.Symbol.DeclarationLocation)
        HashSet(usages)
    let unusedRanges =
        definitions
        |> Array.map (fun defSu -> defSu, usages.Contains defSu.Symbol.DeclarationLocation.Value)
        |> Array.groupBy (fun (defSu, _) -> defSu.Range)
        |> Array.filter (fun (_, defSus) -> defSus |> Array.forall (fun (_, isUsed) -> not isUsed))
        |> Array.map (fun (m, _) -> m)
    unusedRanges