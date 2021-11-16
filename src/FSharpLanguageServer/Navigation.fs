namespace FSharpLanguageServer

// Forked from https://github.com/fsharp/FSharp.Compiler.Service/blob/30127fa32cb0306cb81d95836cfee4ea5c116297/src/fsharp/service/ServiceNavigation.fs
// 2021-04-13:
//      Small adaptation to the file based on https://github.com/dotnet/fsharp/blob/60a2fa663a3c4aed3f03c8bfc6f5e05b04284f23/src/fsharp/service/ServiceNavigation.fs
//      The changes are needed because SyntaxOps functions were no longer exposed by FCS so we had to fork part of that file

// TODO this is a bit fuzzy with nesting---I think this would produce wrong answers with pathological examples like:
// module Outer1 =
//     module Inner =
//         val foo: string
// module Outer2 =
//     module Inner =
//         val foo: string

open FSharp.Compiler.EditorServices
open FSharp.Compiler
open FSharp.Compiler.Text.Range
open FSharp.Compiler.Syntax
open FSharpLanguageServer.SyntaxOps

/// Represents an item to be displayed in the navigation bar
 [<Sealed>]
type MyNavigationItem(uniqueName: string, name: string, kind: NavigationItemKind, glyph: FSharpGlyph, range: Text.range,
                                     bodyRange: Text.range, singleTopLevel: bool, enclosingEntityKind: NavigationEntityKind, isAbstract: bool, access: SynAccess option) =

    member x.bodyRange = bodyRange
    member x.UniqueName = uniqueName
    member x.Name = name
    member x.Glyph = glyph
    member x.Kind = kind
    member x.Range = range
    member x.BodyRange = bodyRange
    member x.IsSingleTopLevel = singleTopLevel
    member x.NavigationEntityKind = enclosingEntityKind
    member x.IsAbstract = isAbstract

    member x.Access = access

    member x.WithUniqueName(uniqueName: string) =
      MyNavigationItem(uniqueName, name, kind, glyph, range, bodyRange, singleTopLevel, enclosingEntityKind, isAbstract, access)
    static member Create(name: string, kind, glyph: FSharpGlyph, range: Text.range, bodyRange: Text.range, singleTopLevel: bool, enclosingEntityKind, isAbstract, access: SynAccess option) =
      MyNavigationItem("", name, kind, glyph, range, bodyRange, singleTopLevel, enclosingEntityKind, isAbstract, access)
 
/// Represents top-level declarations (that should be in the type drop-down)
/// with nested declarations (that can be shown in the member drop-down)
[<NoEquality; NoComparison>]
type FSharpNavigationTopLevelDeclaration =
    { Declaration: MyNavigationItem
      Nested: MyNavigationItem[] }

/// Represents result of 'GetNavigationItems' operation - this contains
/// all the members and currently selected indices. First level correspond to
/// types & modules and second level are methods etc.
[<Sealed>]
type FSharpNavigationItems(declarations:FSharpNavigationTopLevelDeclaration[]) =
    member x.Declarations = declarations

module Navigation =
    
    let unionRangesChecked r1 r2 = if r1 = Text.range.Zero then r2 elif r2 = Text.range.Zero then r1 else unionRanges r1 r2

    let rangeOfDecls2 f decls =
      match (decls |> List.map (f >> (fun (d: MyNavigationItem) -> d.BodyRange))) with
      | hd::tl -> tl |> List.fold unionRangesChecked hd
      | [] -> Text.range.Zero

    let rangeOfDecls = rangeOfDecls2 fst

    let moduleRange (idm:Text.range) others =
      unionRangesChecked idm.EndRange (rangeOfDecls2 (fun (a, _, _) -> a) others)

    let fldspecRange fldspec =
      match fldspec with
      | SynUnionCaseKind.Fields(flds) -> flds |> List.fold (fun st (SynField(_, _, _, _, _, _, _, m)) -> unionRangesChecked m st) Text.range.Zero
      | SynUnionCaseKind.FullType(ty, _) -> ty.Range

    let bodyRange mb decls =
      unionRangesChecked (rangeOfDecls decls) mb

    /// Get information for implementation file
    let getNavigationFromImplFile (modules: SynModuleOrNamespace list) =
        // Map for dealing with name conflicts
        let nameMap = ref Map.empty

        let addItemName name =
            let count = defaultArg (!nameMap |> Map.tryFind name) 0
            nameMap := (Map.add name (count + 1) (!nameMap))
            (count + 1)

        let uniqueName name idx =
            let total = Map.find name (!nameMap)
            sprintf "%s_%d_of_%d" name idx total

        // Create declaration (for the left dropdown)
        let createDeclLid(baseName, lid, kind, baseGlyph, m, bodym, nested, enclosingEntityKind, isAbstract, access) =
            let name = (if baseName <> "" then baseName + "." else "") + (textOfLid lid)
            MyNavigationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested

        let createDecl(baseName, id:Ident, kind, baseGlyph, m, bodym, nested, enclosingEntityKind, isAbstract, access) =
            let name = (if baseName <> "" then baseName + "." else "") + (id.idText)
            MyNavigationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested

        // Create member-kind-of-thing for the right dropdown
        let createMemberLid(lid, kind, baseGlyph, m, enclosingEntityKind, isAbstract, access) =
            MyNavigationItem.Create(textOfLid lid, kind, baseGlyph, m, m, false, enclosingEntityKind, isAbstract, access), (addItemName(textOfLid lid))

        let createMember(id:Ident, kind, baseGlyph, m, enclosingEntityKind, isAbstract, access) =
            MyNavigationItem.Create(id.idText, kind, baseGlyph, m, m, false, enclosingEntityKind, isAbstract, access), (addItemName(id.idText))

        // Process let-binding
        let processBinding isMember enclosingEntityKind isAbstract (SynBinding(_, _, _, _, _, _, SynValData(memebrOpt, _, _), synPat, _, synExpr, _, _)) =
            let m =
                match synExpr with
                | SynExpr.Typed(e, _, _) -> e.Range // fix range for properties with type annotations
                | _ -> synExpr.Range

            match synPat, memebrOpt with
            | SynPat.LongIdent(longDotId=LongIdentWithDots(lid,_); accessibility=access), Some(flags) when isMember ->
                let icon, kind =
                  match flags.MemberKind with
                  | SynMemberKind.ClassConstructor
                  | SynMemberKind.Constructor
                  | SynMemberKind.Member ->
                        (if flags.IsOverrideOrExplicitImpl then FSharpGlyph.OverridenMethod else FSharpGlyph.Method), NavigationItemKind.Method
                  | SynMemberKind.PropertyGetSet
                  | SynMemberKind.PropertySet
                  | SynMemberKind.PropertyGet -> FSharpGlyph.Property, NavigationItemKind.Property
                let lidShow, rangeMerge =
                  match lid with
                  | _thisVar::nm::_ -> (List.tail lid, nm.idRange)
                  | hd::_ -> (lid, hd.idRange)
                  | _ -> (lid, m)
                [ createMemberLid(lidShow, kind, icon, unionRanges rangeMerge m, enclosingEntityKind, isAbstract, access) ]
            | SynPat.LongIdent(LongIdentWithDots(lid,_), _, _, _, access, _), _ ->
                [ createMemberLid(lid, NavigationItemKind.Field, FSharpGlyph.Field, unionRanges (List.head lid).idRange m, enclosingEntityKind, isAbstract, access) ]
            | SynPat.Named( id, _, access, _), _ ->
                let glyph = if isMember then FSharpGlyph.Method else FSharpGlyph.Field
                [ createMember(id, NavigationItemKind.Field, glyph, unionRanges id.idRange m, enclosingEntityKind, isAbstract, access) ]
            | _ -> []

        // Process a class declaration or F# type declaration
        let rec processExnDefnRepr baseName nested (SynExceptionDefnRepr(_, (SynUnionCase(_, id, fldspec, _, _, _)), _, _, access, m)) =
            // Exception declaration
            [ createDecl(baseName, id, NavigationItemKind.Exception, FSharpGlyph.Exception, m, fldspecRange fldspec, nested, NavigationEntityKind.Exception, false, access) ]

        // Process a class declaration or F# type declaration
        and processExnDefn baseName (SynExceptionDefn(repr, membDefns, _)) =
            let nested = processMembers membDefns NavigationEntityKind.Exception |> snd
            processExnDefnRepr baseName nested repr

        and processTycon baseName (SynTypeDefn(SynComponentInfo(_, _, _, lid, _, _, access, _), repr, membDefns,_, m)) =
            let topMembers = processMembers (membDefns) NavigationEntityKind.Class |> snd //TODO: membDefns is sometimes optional now, make sure my fix hasn't stuffed everything up
            match repr with
            | SynTypeDefnRepr.Exception repr -> processExnDefnRepr baseName [] repr
            | SynTypeDefnRepr.ObjectModel(_, membDefns, mb) ->
                // F# class declaration
                let members = processMembers membDefns NavigationEntityKind.Class |> snd
                let nested = members@topMembers
                ([ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Class, m, bodyRange mb nested, nested, NavigationEntityKind.Class, false, access) ]: ((MyNavigationItem * int * _) list))
            | SynTypeDefnRepr.Simple(simple, _) ->
                // F# type declaration
                match simple with
                | SynTypeDefnSimpleRepr.Union(_, cases, mb) ->
                    let cases =
                        [ for (SynUnionCase(_, id, fldspec, _, _, _)) in cases ->
                            createMember(id, NavigationItemKind.Other, FSharpGlyph.Struct, unionRanges (fldspecRange fldspec) id.idRange, NavigationEntityKind.Union, false, access) ]
                    let nested = cases@topMembers
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Union, m, bodyRange mb nested, nested, NavigationEntityKind.Union, false, access) ]
                | SynTypeDefnSimpleRepr.Enum(cases, mb) ->
                    let cases =
                        [ for (SynEnumCase(_, id, _, _,_, m)) in cases ->
                            createMember(id, NavigationItemKind.Field, FSharpGlyph.EnumMember, m, NavigationEntityKind.Enum, false, access) ]
                    let nested = cases@topMembers
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Enum, m, bodyRange mb nested, nested, NavigationEntityKind.Enum, false, access) ]
                | SynTypeDefnSimpleRepr.Record(_, fields, mb) ->
                    let fields =
                        [ for (SynField(_, _, id, _, _, _, _, m)) in fields do
                            if (id.IsSome) then
                              yield createMember(id.Value, NavigationItemKind.Field, FSharpGlyph.Field, m, NavigationEntityKind.Record, false, access) ]
                    let nested = fields@topMembers
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Type, m, bodyRange mb nested, nested, NavigationEntityKind.Record, false, access) ]
                | SynTypeDefnSimpleRepr.TypeAbbrev(_, _, mb) ->
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Typedef, m, bodyRange mb topMembers, topMembers, NavigationEntityKind.Class, false, access) ]

                //| SynTypeDefnSimpleRepr.General of TyconKind * (SynType * range * ident option) list * (valSpfn * MemberFlags) list * NavigationItemKind.Fields * bool * bool * range
                //| SynTypeDefnSimpleRepr.LibraryOnlyILAssembly of ILType * range
                //| TyconCore_repr_hidden of range
                | _ -> []

        // Returns class-members for the right dropdown
        and processMembers members enclosingEntityKind : (Text.range * list<MyNavigationItem * int>) =
            let members =
                members
                |> List.groupBy (fun x -> x.Range)
                |> List.map (fun (range, members) ->
                    range,
                    (match members with
                     | [memb] ->
                         match memb with
                         | SynMemberDefn.LetBindings(binds, _, _, _) -> List.collect (processBinding false enclosingEntityKind false) binds
                         | SynMemberDefn.Member(bind, _) -> processBinding true enclosingEntityKind false bind
                         | SynMemberDefn.ValField(SynField(_, _, Some(rcid), ty, _, _, access, _), _) ->
                             [ createMember(rcid, NavigationItemKind.Field, FSharpGlyph.Field, ty.Range, enclosingEntityKind, false, access) ]
                         | SynMemberDefn.AutoProperty(_attribs,_isStatic,id,_tyOpt,_propKind,_,_xmlDoc, access,_synExpr, _, _) ->
                             [ createMember(id, NavigationItemKind.Field, FSharpGlyph.Field, id.idRange, enclosingEntityKind, false, access) ]
                         | SynMemberDefn.AbstractSlot(SynValSig(_, id, _, ty, _, _, _, _, access, _, _), _, _) ->
                             [ createMember(id, NavigationItemKind.Method, FSharpGlyph.OverridenMethod, ty.Range, enclosingEntityKind, true, access) ]
                         | SynMemberDefn.NestedType _ -> failwith "tycon as member????" //processTycon tycon
                         | SynMemberDefn.Interface(_, Some(membs), _) ->
                             processMembers membs enclosingEntityKind |> snd
                         | _ -> []
                     // can happen if one is a getter and one is a setter
                     | [SynMemberDefn.Member(memberDefn=SynBinding(headPat=SynPat.LongIdent(lid1, Some(info1),_,_,_,_)) as binding1)
                        SynMemberDefn.Member(memberDefn=SynBinding(headPat=SynPat.LongIdent(lid2, Some(info2),_,_,_,_)) as binding2)] ->
                         // ensure same long id
                         assert((lid1.Lid,lid2.Lid) ||> List.forall2 (fun x y -> x.idText = y.idText))
                         // ensure one is getter, other is setter
                         assert((info1.idText = "set" && info2.idText = "get") ||
                                (info2.idText = "set" && info1.idText = "get"))
                         // both binding1 and binding2 have same range, so just try the first one, else try the second one
                         match processBinding true enclosingEntityKind false binding1 with
                         | [] -> processBinding true enclosingEntityKind false binding2
                         | x -> x
                     | _ -> []))

            (members |> Seq.map fst |> Seq.fold unionRangesChecked Text.range.Zero),
            (members |> List.map snd |> List.concat)

        // Process declarations in a module that belong to the right drop-down (let bindings)
        let processNestedDeclarations decls = decls |> List.collect (function
            | SynModuleDecl.Let(_, binds, _) -> List.collect (processBinding false NavigationEntityKind.Module false) binds
            | _ -> [])

        // Process declarations nested in a module that should be displayed in the left dropdown
        // (such as type declarations, nested modules etc.)
        let rec processFSharpNavigationTopLevelDeclarations(baseName, decls) = decls |> List.collect (function
            | SynModuleDecl.ModuleAbbrev(id, lid, m) ->
                [ createDecl(baseName, id, NavigationItemKind.Module, FSharpGlyph.Module, m, rangeOfLid lid, [], NavigationEntityKind.Namespace, false, None) ]

            | SynModuleDecl.NestedModule(SynComponentInfo(_, _, _, lid, _, _, access, _), _isRec, decls, _, m) ->
                // Find let bindings (for the right dropdown)
                let nested = processNestedDeclarations(decls)
                let newBaseName = (if (baseName = "") then "" else baseName+".") + (textOfLid lid)

                // Get nested modules and types (for the left dropdown)
                let other = processFSharpNavigationTopLevelDeclarations(newBaseName, decls)
                createDeclLid(baseName, lid, NavigationItemKind.Module, FSharpGlyph.Module, m, unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid lid) other), nested, NavigationEntityKind.Module, false, access) :: other

            | SynModuleDecl.Types(tydefs, _) -> tydefs |> List.collect (processTycon baseName)
            | SynModuleDecl.Exception (defn,_) -> processExnDefn baseName defn
            | _ -> [])

        // Collect all the items
        let items =
            // Show base name for this module only if it's not the root one
            let singleTopLevel = (modules.Length = 1)
            modules |> List.collect (fun (SynModuleOrNamespace(id, _isRec, kind, decls, _, _, access, m)) ->
                let isModule =
                    match kind with
                    | SynModuleOrNamespaceKind.AnonModule | SynModuleOrNamespaceKind.NamedModule -> true
                    | _ -> false
                let baseName = if (not singleTopLevel) then textOfLid id else ""
                // Find let bindings (for the right dropdown)
                let nested = processNestedDeclarations(decls)
                // Get nested modules and types (for the left dropdown)
                let other = processFSharpNavigationTopLevelDeclarations(baseName, decls)

                // Create explicitly - it can be 'single top level' thing that is hidden
                match id with
                | [] -> other
                | _ ->
                    let decl =
                        MyNavigationItem.Create
                            (textOfLid id, (if isModule then NavigationItemKind.ModuleFile else NavigationItemKind.Namespace),
                                FSharpGlyph.Module, m,
                                unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid id) other),
                                singleTopLevel, NavigationEntityKind.Module, false, access), (addItemName(textOfLid id)), nested
                    decl::other)

        let items =
            items
            |> Array.ofList
            |> Array.map (fun (d, idx, nest) ->
                let nest = nest |> Array.ofList |> Array.map (fun (decl, idx) -> decl.WithUniqueName(uniqueName d.Name idx))
                nest |> Array.sortInPlaceWith (fun a b -> compare a.Name b.Name)
                { Declaration = d.WithUniqueName(uniqueName d.Name idx); Nested = nest } )
        items |> Array.sortInPlaceWith (fun a b -> compare a.Declaration.Name b.Declaration.Name)
        new FSharpNavigationItems(items)

    /// Get information for signature file
    let getNavigationFromSigFile (modules: SynModuleOrNamespaceSig list) =
        // Map for dealing with name conflicts
        let nameMap = ref Map.empty
        let addItemName name =
            let count = defaultArg (!nameMap |> Map.tryFind name) 0
            nameMap := (Map.add name (count + 1) (!nameMap))
            (count + 1)
        let uniqueName name idx =
            let total = Map.find name (!nameMap)
            sprintf "%s_%d_of_%d" name idx total

        // Create declaration (for the left dropdown)
        let createDeclLid(baseName, lid, kind, baseGlyph, m, bodym, nested, enclosingEntityKind, isAbstract, access) =
            let name = (if baseName <> "" then baseName + "." else "") + (textOfLid lid)
            MyNavigationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested

        let createDecl(baseName, id:Ident, kind, baseGlyph, m, bodym, nested, enclosingEntityKind, isAbstract, access) =
            let name = (if baseName <> "" then baseName + "." else "") + (id.idText)
            MyNavigationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested

        let createMember(id:Ident, kind, baseGlyph, m, enclosingEntityKind, isAbstract, access) =
            MyNavigationItem.Create(id.idText, kind, baseGlyph, m, m, false, enclosingEntityKind, isAbstract, access), (addItemName(id.idText))

        let rec processExnRepr baseName nested (SynExceptionDefnRepr(_, (SynUnionCase(_, id, fldspec, _, _, _)), _, _, access, m)) =
            // Exception declaration
            [ createDecl(baseName, id, NavigationItemKind.Exception, FSharpGlyph.Exception, m, fldspecRange fldspec, nested, NavigationEntityKind.Exception, false, access) ]

        and processExnSig baseName (SynExceptionSig(repr, memberSigs, _)) =
            let nested = processSigMembers memberSigs
            processExnRepr baseName nested repr

        and processTycon baseName (SynTypeDefnSig(SynComponentInfo(_, _, _, lid, _, _, access, _), repr, membDefns, m)) =
            let topMembers = processSigMembers membDefns
            match repr with
            | SynTypeDefnSigRepr.Exception repr -> processExnRepr baseName [] repr
            | SynTypeDefnSigRepr.ObjectModel(_, membDefns, mb) ->
                // F# class declaration
                let members = processSigMembers membDefns
                let nested = members @ topMembers
                ([ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Class, m, bodyRange mb nested, nested, NavigationEntityKind.Class, false, access) ]: ((MyNavigationItem * int * _) list))
            | SynTypeDefnSigRepr.Simple(simple, _) ->
                // F# type declaration
                match simple with
                | SynTypeDefnSimpleRepr.Union(_, cases, mb) ->
                    let cases =
                        [ for (SynUnionCase(_, id, fldspec, _, _, _)) in cases ->
                            createMember(id, NavigationItemKind.Other, FSharpGlyph.Struct, unionRanges (fldspecRange fldspec) id.idRange, NavigationEntityKind.Union, false, access) ]
                    let nested = cases@topMembers
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Union, m, bodyRange mb nested, nested, NavigationEntityKind.Union, false, access) ]
                | SynTypeDefnSimpleRepr.Enum(cases, mb) ->
                    let cases =
                        [ for (SynEnumCase(_, id, _, _,_,m )) in cases -> 
                            createMember(id, NavigationItemKind.Field, FSharpGlyph.EnumMember, m, NavigationEntityKind.Enum, false, access) ]
                    let nested = cases@topMembers
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Enum, m, bodyRange mb nested, nested, NavigationEntityKind.Enum, false, access) ]
                | SynTypeDefnSimpleRepr.Record(_, fields, mb) ->
                    let fields =
                        [ for (SynField(_, _, id, _, _, _, _, m)) in fields do
                            if (id.IsSome) then
                              yield createMember(id.Value, NavigationItemKind.Field, FSharpGlyph.Field, m, NavigationEntityKind.Record, false, access) ]
                    let nested = fields@topMembers
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Type, m, bodyRange mb nested, nested, NavigationEntityKind.Record, false, access) ]
                | SynTypeDefnSimpleRepr.TypeAbbrev(_, _, mb) ->
                    [ createDeclLid(baseName, lid, NavigationItemKind.Type, FSharpGlyph.Typedef, m, bodyRange mb topMembers, topMembers, NavigationEntityKind.Class, false, access) ]

                //| SynTypeDefnSimpleRepr.General of TyconKind * (SynType * range * ident option) list * (valSpfn * MemberFlags) list * NavigationItemKind.Fields * bool * bool * range
                //| SynTypeDefnSimpleRepr.LibraryOnlyILAssembly of ILType * range
                //| TyconCore_repr_hidden of range
                | _ -> []

        and processSigMembers (members: SynMemberSig list): list<MyNavigationItem * int> =
            [ for memb in members do
                 match memb with
                 | SynMemberSig.Member(SynValSig.SynValSig(_, id, _, _, _, _, _, _, access, _, m), _, _) ->
                     yield createMember(id, NavigationItemKind.Method, FSharpGlyph.Method, m, NavigationEntityKind.Class, false, access)
                 | SynMemberSig.ValField(SynField(_, _, Some(rcid), ty, _, _, access, _), _) ->
                     yield createMember(rcid, NavigationItemKind.Field, FSharpGlyph.Field, ty.Range, NavigationEntityKind.Class, false, access)
                 | _ -> () ]

        // Process declarations in a module that belong to the right drop-down (let bindings)
        let processNestedSigDeclarations decls = decls |> List.collect (function
            | SynModuleSigDecl.Val(SynValSig.SynValSig(_, id, _, _, _, _, _, _, access, _, m), _) ->
                [ createMember(id, NavigationItemKind.Method, FSharpGlyph.Method, m, NavigationEntityKind.Module, false, access) ]
            | _ -> [] )

        // Process declarations nested in a module that should be displayed in the left dropdown
        // (such as type declarations, nested modules etc.)
        let rec processFSharpNavigationTopLevelSigDeclarations(baseName, decls) = decls |> List.collect (function
            | SynModuleSigDecl.ModuleAbbrev(id, lid, m) ->
                [ createDecl(baseName, id, NavigationItemKind.Module, FSharpGlyph.Module, m, rangeOfLid lid, [], NavigationEntityKind.Module, false, None) ]

            | SynModuleSigDecl.NestedModule(SynComponentInfo(_, _, _, lid, _, _, access, _), _, decls, m) ->
                // Find let bindings (for the right dropdown)
                let nested = processNestedSigDeclarations(decls)
                let newBaseName = (if (baseName = "") then "" else baseName+".") + (textOfLid lid)

                // Get nested modules and types (for the left dropdown)
                let other = processFSharpNavigationTopLevelSigDeclarations(newBaseName, decls)
                createDeclLid(baseName, lid, NavigationItemKind.Module, FSharpGlyph.Module, m, unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid lid) other), nested, NavigationEntityKind.Module, false, access)::other

            | SynModuleSigDecl.Types(tydefs, _) -> tydefs |> List.collect (processTycon baseName)
            | SynModuleSigDecl.Exception (defn,_) -> processExnSig baseName defn
            | _ -> [])

        // Collect all the items
        let items =
            // Show base name for this module only if it's not the root one
            let singleTopLevel = (modules.Length = 1)
            modules |> List.collect (fun (SynModuleOrNamespaceSig(id, _isRec, kind, decls, _, _, access, m)) ->
                let isModule =
                    match kind with
                    | SynModuleOrNamespaceKind.AnonModule | SynModuleOrNamespaceKind.NamedModule -> true
                    | _ -> false
                let baseName = if (not singleTopLevel) then textOfLid id else ""
                // Find let bindings (for the right dropdown)
                let nested = processNestedSigDeclarations(decls)
                // Get nested modules and types (for the left dropdown)
                let other = processFSharpNavigationTopLevelSigDeclarations(baseName, decls)

                // Create explicitly - it can be 'single top level' thing that is hidden
                let decl =
                    MyNavigationItem.Create
                        (textOfLid id, (if isModule then NavigationItemKind.ModuleFile else NavigationItemKind.Namespace),
                            FSharpGlyph.Module, m,
                            unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid id) other),
                            singleTopLevel, NavigationEntityKind.Module, false, access), (addItemName(textOfLid id)), nested
                decl::other)

        let items =
            items
            |> Array.ofList
            |> Array.map (fun (d, idx, nest) ->
                let nest = nest |> Array.ofList |> Array.map (fun (decl, idx) -> decl.WithUniqueName(uniqueName d.Name idx))
                nest |> Array.sortInPlaceWith (fun a b -> compare a.Name b.Name)
                let nest = nest |> Array.distinctBy (fun x -> x.Range, x.BodyRange, x.Name, x.Kind)

                { Declaration = d.WithUniqueName(uniqueName d.Name idx); Nested = nest } )
        items |> Array.sortInPlaceWith (fun a b -> compare a.Declaration.Name b.Declaration.Name)
        new FSharpNavigationItems(items)
