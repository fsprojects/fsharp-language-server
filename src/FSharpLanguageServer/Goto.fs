module  FSharpLanguageServer.Goto
let _TryFindIdentifierDeclaration (pos: Pos) (lineStr: LineStr) =
    match Lexer.findLongIdents(pos.Column, lineStr) with
    | None -> async.Return (ResultOrString.Error "Could not find ident at this location")
    | Some(col, identIsland) ->
      let identIsland = Array.toList identIsland
      let declarations = checkResults.GetDeclarationLocation(pos.Line, col, lineStr, identIsland, preferFlag = false)

      let decompile assembly externalSym =
        match Decompiler.tryFindExternalDeclaration checkResults (assembly, externalSym) with
        | Ok extDec -> ResultOrString.Ok (FindDeclarationResult.ExternalDeclaration extDec)
        | Error(Decompiler.FindExternalDeclarationError.ReferenceHasNoFileName assy) -> ResultOrString.Error (sprintf "External declaration assembly '%s' missing file name" assy.SimpleName)
        | Error(Decompiler.FindExternalDeclarationError.ReferenceNotFound assy) -> ResultOrString.Error (sprintf "External declaration assembly '%s' not found" assy)
        | Error(Decompiler.FindExternalDeclarationError.DecompileError (Decompiler.Exception(symbol, file, exn))) ->
          Error (sprintf "Error while decompiling symbol '%A' in file '%s': %s\n%s" symbol file exn.Message exn.StackTrace)

      /// these are all None because you can't easily get the source file from the external symbol information here.
      let tryGetSourceRangeForSymbol (sym: FSharpExternalSymbol): (string<NormalizedRepoPathSegment> * Pos) option =
        match sym with
        | FSharpExternalSymbol.Type name -> None
        | FSharpExternalSymbol.Constructor(typeName, args) -> None
        | FSharpExternalSymbol.Method(typeName, name, paramSyms, genericArity) -> None
        | FSharpExternalSymbol.Field(typeName, name) -> None
        | FSharpExternalSymbol.Event(typeName, name) -> None
        | FSharpExternalSymbol.Property(typeName, name) -> None

      // attempts to manually discover symbol use and externalsymbol information for a range that doesn't exist in a local file
      // bugfix/workaround for FCS returning invalid declfound for f# members.
      let tryRecoverExternalSymbolForNonexistentDecl (rangeInNonexistentFile: Range): ResultOrString<string<LocalPath> * string<NormalizedRepoPathSegment>> =
        match Lexer.findLongIdents(pos.Column - 1, lineStr) with
        | None -> ResultOrString.Error (sprintf "Range for nonexistent file found, no ident found: %s" rangeInNonexistentFile.FileName)
        | Some (col, identIsland) ->
          let identIsland = Array.toList identIsland
          let symbolUse = checkResults.GetSymbolUseAtLocation(pos.Line, col, lineStr, identIsland)
          match symbolUse with
          | None -> ResultOrString.Error (sprintf "Range for nonexistent file found, no symboluse found: %s" rangeInNonexistentFile.FileName)
          | Some sym ->
            match sym.Symbol.Assembly.FileName with
            | Some fullFilePath ->
              Ok (UMX.tag<LocalPath> fullFilePath, UMX.tag<NormalizedRepoPathSegment> rangeInNonexistentFile.FileName)
            | None ->
              ResultOrString.Error (sprintf "Assembly '%s' declaring symbol '%s' has no location on disk" sym.Symbol.Assembly.QualifiedName sym.Symbol.DisplayName)

      async {
        match declarations with
        | FSharpFindDeclResult.DeclNotFound reason ->
          let elaboration =
            match reason with
            | FSharpFindDeclFailureReason.NoSourceCode -> "No source code was found for the declaration"
            | FSharpFindDeclFailureReason.ProvidedMember m -> sprintf "Go-to-declaration is not available for Type Provider-provided member %s" m
            | FSharpFindDeclFailureReason.ProvidedType t -> sprintf "Go-to-declaration is not available from Type Provider-provided type %s" t
            | FSharpFindDeclFailureReason.Unknown r -> r
          return ResultOrString.Error (sprintf "Could not find declaration. %s" elaboration)
        | FSharpFindDeclResult.DeclFound range when range.FileName.EndsWith(Range.rangeStartup.FileName) -> return ResultOrString.Error "Could not find declaration"
        | FSharpFindDeclResult.DeclFound range when System.IO.File.Exists range.FileName ->
          let rangeStr = range.ToString()
          logger.info (Log.setMessage "Got a declresult of {range} that supposedly exists" >> Log.addContextDestructured "range" rangeStr)
          return Ok (FindDeclarationResult.Range range)
        | FSharpFindDeclResult.DeclFound rangeInNonexistentFile ->
          let range = rangeInNonexistentFile.ToString()
          logger.warn (Log.setMessage "Got a declresult of {range} that doesn't exist" >> Log.addContextDestructured "range" range)
          match tryRecoverExternalSymbolForNonexistentDecl rangeInNonexistentFile with
          | Ok (assemblyFile, sourceFile) ->
            match! Sourcelink.tryFetchSourcelinkFile assemblyFile sourceFile with
            | Ok localFilePath ->
              return ResultOrString.Ok (FindDeclarationResult.ExternalDeclaration { File = UMX.untag localFilePath; Position = rangeInNonexistentFile.Start })
            | Error reason ->
              return ResultOrString.Error (sprintf "%A" reason)
          | Error e -> return Error e
        | FSharpFindDeclResult.ExternalDecl (assembly, externalSym) ->
          // not enough info on external symbols to get a range-like thing :(
          match tryGetSourceRangeForSymbol externalSym with
          | Some (sourceFile, pos) ->
            match! Sourcelink.tryFetchSourcelinkFile (UMX.tag<LocalPath> assembly) sourceFile with
            | Ok localFilePath ->
              return ResultOrString.Ok (FindDeclarationResult.ExternalDeclaration { File = UMX.untag localFilePath; Position = pos })
            | Error reason ->
              logger.info (Log.setMessage "no sourcelink info for {assembly}, decompiling instead" >> Log.addContextDestructured "assembly" assembly)
              return decompile assembly externalSym
          | None ->
            return decompile assembly externalSym
    }