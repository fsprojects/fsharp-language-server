namespace FSharpLanguageServer

// Forked from https://github.com/fsharp/FSharp.Compiler.Service/blob/30127fa32cb0306cb81d95836cfee4ea5c116297/src/fsharp/service/ServiceNavigation.fs

// TODO this is a bit fuzzy with nesting---I think this would produce wrong answers with pathological examples like:
// module Outer1 =
//     module Inner = 
//         val foo: string 
// module Outer2 = 
//     module Inner = 
//         val foo: string

open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler.Range
open Microsoft.FSharp.Compiler.Ast

/// Represents an item to be displayed in the navigation bar
[<Sealed>]
type NavigationDeclarationItem(uniqueName: string, name: string, kind: FSharpNavigationDeclarationItemKind, glyph: FSharpGlyph, range: range, 
                                     bodyRange: range, singleTopLevel: bool, enclosingEntityKind: FSharpEnclosingEntityKind, isAbstract: bool, access: SynAccess option) = 
    
    member x.bodyRange = bodyRange
    member x.UniqueName = uniqueName
    member x.Name = name
    member x.Glyph = glyph
    member x.Kind = kind
    member x.Range = range
    member x.BodyRange = bodyRange 
    member x.IsSingleTopLevel = singleTopLevel
    member x.FSharpEnclosingEntityKind = enclosingEntityKind
    member x.IsAbstract = isAbstract
    
    member x.Access = access
    
    member x.WithUniqueName(uniqueName: string) =
      NavigationDeclarationItem(uniqueName, name, kind, glyph, range, bodyRange, singleTopLevel, enclosingEntityKind, isAbstract, access)
    static member Create(name: string, kind, glyph: FSharpGlyph, range: range, bodyRange: range, singleTopLevel: bool, enclosingEntityKind, isAbstract, access: SynAccess option) = 
      NavigationDeclarationItem("", name, kind, glyph, range, bodyRange, singleTopLevel, enclosingEntityKind, isAbstract, access)

/// Represents top-level declarations (that should be in the type drop-down)
/// with nested declarations (that can be shown in the member drop-down)
[<NoEquality; NoComparison>]
type FSharpNavigationTopLevelDeclaration = 
    { Declaration: NavigationDeclarationItem
      Nested: NavigationDeclarationItem[] }
      
/// Represents result of 'GetNavigationItems' operation - this contains
/// all the members and currently selected indices. First level correspond to
/// types & modules and second level are methods etc.
[<Sealed>]
type FSharpNavigationItems(declarations:FSharpNavigationTopLevelDeclaration[]) =
    member x.Declarations = declarations
      
module Navigation = 
    let unionRangesChecked r1 r2 = if r1 = range.Zero then r2 elif r2 = range.Zero then r1 else unionRanges r1 r2
    
    let rangeOfDecls2 f decls = 
      match (decls |> List.map (f >> (fun (d:NavigationDeclarationItem) -> d.bodyRange))) with 
      | hd::tl -> tl |> List.fold unionRangesChecked hd
      | [] -> range.Zero
    
    let rangeOfDecls = rangeOfDecls2 fst

    let moduleRange (idm:range) others = 
      unionRangesChecked idm.EndRange (rangeOfDecls2 (fun (a, _, _) -> a) others)
    
    let fldspecRange fldspec =
      match fldspec with
      | UnionCaseFields(flds) -> flds |> List.fold (fun st (Field(_, _, _, _, _, _, _, m)) -> unionRangesChecked m st) range.Zero
      | UnionCaseFullType(ty, _) -> ty.Range
      
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
            NavigationDeclarationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested
            
        let createDecl(baseName, id:Ident, kind, baseGlyph, m, bodym, nested, enclosingEntityKind, isAbstract, access) =
            let name = (if baseName <> "" then baseName + "." else "") + (id.idText)
            NavigationDeclarationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested
         
        // Create member-kind-of-thing for the right dropdown
        let createMemberLid(lid, kind, baseGlyph, m, enclosingEntityKind, isAbstract, access) =
            NavigationDeclarationItem.Create(textOfLid lid, kind, baseGlyph, m, m, false, enclosingEntityKind, isAbstract, access), (addItemName(textOfLid lid))

        let createMember(id:Ident, kind, baseGlyph, m, enclosingEntityKind, isAbstract, access) =
            NavigationDeclarationItem.Create(id.idText, kind, baseGlyph, m, m, false, enclosingEntityKind, isAbstract, access), (addItemName(id.idText))

        // Process let-binding
        let processBinding isMember enclosingEntityKind isAbstract (Binding(_, _, _, _, _, _, SynValData(memebrOpt, _, _), synPat, _, synExpr, _, _)) =
            let m = 
                match synExpr with 
                | SynExpr.Typed(e, _, _) -> e.Range // fix range for properties with type annotations
                | _ -> synExpr.Range

            match synPat, memebrOpt with
            | SynPat.LongIdent(longDotId=LongIdentWithDots(lid,_); accessibility=access), Some(flags) when isMember -> 
                let icon, kind =
                  match flags.MemberKind with
                  | MemberKind.ClassConstructor
                  | MemberKind.Constructor
                  | MemberKind.Member -> 
                        (if flags.IsOverrideOrExplicitImpl then FSharpGlyph.OverridenMethod else FSharpGlyph.Method), MethodDecl
                  | MemberKind.PropertyGetSet
                  | MemberKind.PropertySet
                  | MemberKind.PropertyGet -> FSharpGlyph.Property, PropertyDecl
                let lidShow, rangeMerge = 
                  match lid with 
                  | _thisVar::nm::_ -> (List.tail lid, nm.idRange) 
                  | hd::_ -> (lid, hd.idRange) 
                  | _ -> (lid, m)
                [ createMemberLid(lidShow, kind, icon, unionRanges rangeMerge m, enclosingEntityKind, isAbstract, access) ]
            | SynPat.LongIdent(LongIdentWithDots(lid,_), _, _, _, access, _), _ -> 
                [ createMemberLid(lid, FieldDecl, FSharpGlyph.Field, unionRanges (List.head lid).idRange m, enclosingEntityKind, isAbstract, access) ]
            | SynPat.Named(_, id, _, access, _), _ -> 
                let glyph = if isMember then FSharpGlyph.Method else FSharpGlyph.Field
                [ createMember(id, FieldDecl, glyph, unionRanges id.idRange m, enclosingEntityKind, isAbstract, access) ]
            | _ -> []
        
        // Process a class declaration or F# type declaration
        let rec processExnDefnRepr baseName nested (SynExceptionDefnRepr(_, (UnionCase(_, id, fldspec, _, _, _)), _, _, access, m)) =
            // Exception declaration
            [ createDecl(baseName, id, ExnDecl, FSharpGlyph.Exception, m, fldspecRange fldspec, nested, FSharpEnclosingEntityKind.Exception, false, access) ] 

        // Process a class declaration or F# type declaration
        and processExnDefn baseName (SynExceptionDefn(repr, membDefns, _)) =  
            let nested = processMembers membDefns FSharpEnclosingEntityKind.Exception |> snd
            processExnDefnRepr baseName nested repr

        and processTycon baseName (TypeDefn(ComponentInfo(_, _, _, lid, _, _, access, _), repr, membDefns, m)) =
            let topMembers = processMembers membDefns FSharpEnclosingEntityKind.Class |> snd
            match repr with
            | SynTypeDefnRepr.Exception repr -> processExnDefnRepr baseName [] repr
            | SynTypeDefnRepr.ObjectModel(_, membDefns, mb) ->
                // F# class declaration
                let members = processMembers membDefns FSharpEnclosingEntityKind.Class |> snd
                let nested = members@topMembers
                ([ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Class, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.Class, false, access) ]: ((NavigationDeclarationItem * int * _) list))
            | SynTypeDefnRepr.Simple(simple, _) ->
                // F# type declaration
                match simple with
                | SynTypeDefnSimpleRepr.Union(_, cases, mb) ->
                    let cases = 
                        [ for (UnionCase(_, id, fldspec, _, _, _)) in cases -> 
                            createMember(id, OtherDecl, FSharpGlyph.Struct, unionRanges (fldspecRange fldspec) id.idRange, FSharpEnclosingEntityKind.DU, false, access) ]
                    let nested = cases@topMembers              
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Union, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.DU, false, access) ]
                | SynTypeDefnSimpleRepr.Enum(cases, mb) -> 
                    let cases = 
                        [ for (EnumCase(_, id, _, _, m)) in cases ->
                            createMember(id, FieldDecl, FSharpGlyph.EnumMember, m, FSharpEnclosingEntityKind.Enum, false, access) ]
                    let nested = cases@topMembers
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Enum, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.Enum, false, access) ]
                | SynTypeDefnSimpleRepr.Record(_, fields, mb) ->
                    let fields = 
                        [ for (Field(_, _, id, _, _, _, _, m)) in fields do
                            if (id.IsSome) then
                              yield createMember(id.Value, FieldDecl, FSharpGlyph.Field, m, FSharpEnclosingEntityKind.Record, false, access) ]
                    let nested = fields@topMembers
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Type, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.Record, false, access) ]
                | SynTypeDefnSimpleRepr.TypeAbbrev(_, _, mb) ->
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Typedef, m, bodyRange mb topMembers, topMembers, FSharpEnclosingEntityKind.Class, false, access) ]
                          
                //| SynTypeDefnSimpleRepr.General of TyconKind * (SynType * range * ident option) list * (valSpfn * MemberFlags) list * fieldDecls * bool * bool * range 
                //| SynTypeDefnSimpleRepr.LibraryOnlyILAssembly of ILType * range
                //| TyconCore_repr_hidden of range
                | _ -> [] 
                  
        // Returns class-members for the right dropdown                  
        and processMembers members enclosingEntityKind : (range * list<NavigationDeclarationItem * int>) = 
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
                         | SynMemberDefn.ValField(Field(_, _, Some(rcid), ty, _, _, access, _), _) ->
                             [ createMember(rcid, FieldDecl, FSharpGlyph.Field, ty.Range, enclosingEntityKind, false, access) ]
                         | SynMemberDefn.AutoProperty(_attribs,_isStatic,id,_tyOpt,_propKind,_,_xmlDoc, access,_synExpr, _, _) -> 
                             [ createMember(id, FieldDecl, FSharpGlyph.Field, id.idRange, enclosingEntityKind, false, access) ]
                         | SynMemberDefn.AbstractSlot(ValSpfn(_, id, _, ty, _, _, _, _, access, _, _), _, _) ->
                             [ createMember(id, MethodDecl, FSharpGlyph.OverridenMethod, ty.Range, enclosingEntityKind, true, access) ]
                         | SynMemberDefn.NestedType _ -> failwith "tycon as member????" //processTycon tycon                
                         | SynMemberDefn.Interface(_, Some(membs), _) ->
                             processMembers membs enclosingEntityKind |> snd
                         | _ -> [] 
                     // can happen if one is a getter and one is a setter
                     | [SynMemberDefn.Member(memberDefn=Binding(headPat=SynPat.LongIdent(lid1, Some(info1),_,_,_,_)) as binding1)
                        SynMemberDefn.Member(memberDefn=Binding(headPat=SynPat.LongIdent(lid2, Some(info2),_,_,_,_)) as binding2)] ->
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
            
            (members |> Seq.map fst |> Seq.fold unionRangesChecked range.Zero),
            (members |> List.map snd |> List.concat)

        // Process declarations in a module that belong to the right drop-down (let bindings)
        let processNestedDeclarations decls = decls |> List.collect (function
            | SynModuleDecl.Let(_, binds, _) -> List.collect (processBinding false FSharpEnclosingEntityKind.Module false) binds
            | _ -> [])        

        // Process declarations nested in a module that should be displayed in the left dropdown
        // (such as type declarations, nested modules etc.)                            
        let rec processFSharpNavigationTopLevelDeclarations(baseName, decls) = decls |> List.collect (function
            | SynModuleDecl.ModuleAbbrev(id, lid, m) ->
                [ createDecl(baseName, id, ModuleDecl, FSharpGlyph.Module, m, rangeOfLid lid, [], FSharpEnclosingEntityKind.Namespace, false, None) ]
                
            | SynModuleDecl.NestedModule(ComponentInfo(_, _, _, lid, _, _, access, _), _isRec, decls, _, m) ->
                // Find let bindings (for the right dropdown)
                let nested = processNestedDeclarations(decls)
                let newBaseName = (if (baseName = "") then "" else baseName+".") + (textOfLid lid)
                
                // Get nested modules and types (for the left dropdown)
                let other = processFSharpNavigationTopLevelDeclarations(newBaseName, decls)
                createDeclLid(baseName, lid, ModuleDecl, FSharpGlyph.Module, m, unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid lid) other), nested, FSharpEnclosingEntityKind.Module, false, access) :: other
                  
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
                        NavigationDeclarationItem.Create
                            (textOfLid id, (if isModule then ModuleFileDecl else NamespaceDecl),
                                FSharpGlyph.Module, m, 
                                unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid id) other), 
                                singleTopLevel, FSharpEnclosingEntityKind.Module, false, access), (addItemName(textOfLid id)), nested
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
            NavigationDeclarationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested
            
        let createDecl(baseName, id:Ident, kind, baseGlyph, m, bodym, nested, enclosingEntityKind, isAbstract, access) =
            let name = (if baseName <> "" then baseName + "." else "") + (id.idText)
            NavigationDeclarationItem.Create
              (name, kind, baseGlyph, m, bodym, false, enclosingEntityKind, isAbstract, access), (addItemName name), nested
         
        let createMember(id:Ident, kind, baseGlyph, m, enclosingEntityKind, isAbstract, access) =
            NavigationDeclarationItem.Create(id.idText, kind, baseGlyph, m, m, false, enclosingEntityKind, isAbstract, access), (addItemName(id.idText))

        let rec processExnRepr baseName nested (SynExceptionDefnRepr(_, (UnionCase(_, id, fldspec, _, _, _)), _, _, access, m)) =
            // Exception declaration
            [ createDecl(baseName, id, ExnDecl, FSharpGlyph.Exception, m, fldspecRange fldspec, nested, FSharpEnclosingEntityKind.Exception, false, access) ] 
        
        and processExnSig baseName (SynExceptionSig(repr, memberSigs, _)) =  
            let nested = processSigMembers memberSigs
            processExnRepr baseName nested repr

        and processTycon baseName (TypeDefnSig(ComponentInfo(_, _, _, lid, _, _, access, _), repr, membDefns, m)) =
            let topMembers = processSigMembers membDefns
            match repr with
            | SynTypeDefnSigRepr.Exception repr -> processExnRepr baseName [] repr
            | SynTypeDefnSigRepr.ObjectModel(_, membDefns, mb) ->
                // F# class declaration
                let members = processSigMembers membDefns
                let nested = members @ topMembers
                ([ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Class, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.Class, false, access) ]: ((NavigationDeclarationItem * int * _) list))
            | SynTypeDefnSigRepr.Simple(simple, _) ->
                // F# type declaration
                match simple with
                | SynTypeDefnSimpleRepr.Union(_, cases, mb) ->
                    let cases = 
                        [ for (UnionCase(_, id, fldspec, _, _, _)) in cases -> 
                            createMember(id, OtherDecl, FSharpGlyph.Struct, unionRanges (fldspecRange fldspec) id.idRange, FSharpEnclosingEntityKind.DU, false, access) ]
                    let nested = cases@topMembers              
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Union, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.DU, false, access) ]
                | SynTypeDefnSimpleRepr.Enum(cases, mb) -> 
                    let cases = 
                        [ for (EnumCase(_, id, _, _, m)) in cases ->
                            createMember(id, FieldDecl, FSharpGlyph.EnumMember, m, FSharpEnclosingEntityKind.Enum, false, access) ]
                    let nested = cases@topMembers
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Enum, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.Enum, false, access) ]
                | SynTypeDefnSimpleRepr.Record(_, fields, mb) ->
                    let fields = 
                        [ for (Field(_, _, id, _, _, _, _, m)) in fields do
                            if (id.IsSome) then
                              yield createMember(id.Value, FieldDecl, FSharpGlyph.Field, m, FSharpEnclosingEntityKind.Record, false, access) ]
                    let nested = fields@topMembers
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Type, m, bodyRange mb nested, nested, FSharpEnclosingEntityKind.Record, false, access) ]
                | SynTypeDefnSimpleRepr.TypeAbbrev(_, _, mb) ->
                    [ createDeclLid(baseName, lid, TypeDecl, FSharpGlyph.Typedef, m, bodyRange mb topMembers, topMembers, FSharpEnclosingEntityKind.Class, false, access) ]
                          
                //| SynTypeDefnSimpleRepr.General of TyconKind * (SynType * range * ident option) list * (valSpfn * MemberFlags) list * fieldDecls * bool * bool * range 
                //| SynTypeDefnSimpleRepr.LibraryOnlyILAssembly of ILType * range
                //| TyconCore_repr_hidden of range
                | _ -> [] 
                  
        and processSigMembers (members: SynMemberSig list): list<NavigationDeclarationItem * int> = 
            [ for memb in members do
                 match memb with
                 | SynMemberSig.Member(SynValSig.ValSpfn(_, id, _, _, _, _, _, _, access, _, m), _, _) ->
                     yield createMember(id, MethodDecl, FSharpGlyph.Method, m, FSharpEnclosingEntityKind.Class, false, access)
                 | SynMemberSig.ValField(Field(_, _, Some(rcid), ty, _, _, access, _), _) ->
                     yield createMember(rcid, FieldDecl, FSharpGlyph.Field, ty.Range, FSharpEnclosingEntityKind.Class, false, access)
                 | _ -> () ]

        // Process declarations in a module that belong to the right drop-down (let bindings)
        let processNestedSigDeclarations decls = decls |> List.collect (function
            | SynModuleSigDecl.Val(SynValSig.ValSpfn(_, id, _, _, _, _, _, _, access, _, m), _) ->
                [ createMember(id, MethodDecl, FSharpGlyph.Method, m, FSharpEnclosingEntityKind.Module, false, access) ]
            | _ -> [] )        

        // Process declarations nested in a module that should be displayed in the left dropdown
        // (such as type declarations, nested modules etc.)                            
        let rec processFSharpNavigationTopLevelSigDeclarations(baseName, decls) = decls |> List.collect (function
            | SynModuleSigDecl.ModuleAbbrev(id, lid, m) ->
                [ createDecl(baseName, id, ModuleDecl, FSharpGlyph.Module, m, rangeOfLid lid, [], FSharpEnclosingEntityKind.Module, false, None) ]
                
            | SynModuleSigDecl.NestedModule(ComponentInfo(_, _, _, lid, _, _, access, _), _, decls, m) ->                
                // Find let bindings (for the right dropdown)
                let nested = processNestedSigDeclarations(decls)
                let newBaseName = (if (baseName = "") then "" else baseName+".") + (textOfLid lid)
                
                // Get nested modules and types (for the left dropdown)
                let other = processFSharpNavigationTopLevelSigDeclarations(newBaseName, decls)
                createDeclLid(baseName, lid, ModuleDecl, FSharpGlyph.Module, m, unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid lid) other), nested, FSharpEnclosingEntityKind.Module, false, access)::other
                  
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
                    NavigationDeclarationItem.Create
                        (textOfLid id, (if isModule then ModuleFileDecl else NamespaceDecl),
                            FSharpGlyph.Module, m, 
                            unionRangesChecked (rangeOfDecls nested) (moduleRange (rangeOfLid id) other), 
                            singleTopLevel, FSharpEnclosingEntityKind.Module, false, access), (addItemName(textOfLid id)), nested
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