coc.nvim settings
-----------------

We can specify project-wide settings by creating `.vim/coc-settings.json`  in the project directory.


## Setting build parameters

The default project cracker may not infer the correct build parameters (MSBuild properties, fsc flags, etc).
To circumvent this problem, alter `fsharp.project.define` and `fsharp.project.otherflags` .
The former is a simple array of conditional compilation flags passed to MSBuild.
The latter is lower-level, and consists of flags passed directly to the F# compiler `fsc`.
The best way to find out the compile parameters is to actually build the project with Visual Studio or `dotnet` command line.
Setting the build output verbosity to `normal` or above, and the `fsc` compiler flags will be displayed.
For example, to get the F# core library project, `FSharp.Core.fsproj` to typecheck, we will need the following configuration:

```json
{
    "fsharp.codelens.references": false,
    "fsharp.project.otherFlags": [
        "--warnon:1182",
        "--compiling-fslib",
        "--compiling-fslib-40",
        "--maxerrors:20",
        "--extraoptimizationloops:1",
        "--tailcalls+",
        "--deterministic+",
        "--target:library",
        "--fullpaths",
        "--flaterrors",
        "--highentropyva+",
        "--targetprofile:netcore",
        "--simpleresolution",
        "-g",
        "--debug:portable",
        "--noframework",
        "--define:TRACE",
        "--define:FSHARP_CORE",
        "--define:DEBUG",
        "--define:NETSTANDARD",
        "--define:FX_NO_APP_DOMAINS",
        "--define:FX_NO_ARRAY_LONG_LENGTH",
        "--define:FX_NO_BEGINEND_READWRITE",
        "--define:FX_NO_BINARY_SERIALIZATION",
        "--define:FX_NO_CONVERTER",
        "--define:FX_NO_DEFAULT_DEPENDENCY_TYPE",
        "--define:FX_NO_CORHOST_SIGNER",
        "--define:FX_NO_EVENTWAITHANDLE_IDISPOSABLE",
        "--define:FX_NO_EXIT_CONTEXT_FLAGS",
        "--define:FX_NO_LINKEDRESOURCES",
        "--define:FX_NO_PARAMETERIZED_THREAD_START",
        "--define:FX_NO_PDB_READER",
        "--define:FX_NO_PDB_WRITER",
        "--define:FX_NO_REFLECTION_MODULE_HANDLES",
        "--define:FX_NO_REFLECTION_ONLY",
        "--define:FX_NO_RUNTIMEENVIRONMENT",
        "--define:FX_NO_SECURITY_PERMISSIONS",
        "--define:FX_NO_SERVERCODEPAGES",
        "--define:FX_NO_SYMBOLSTORE",
        "--define:FX_NO_SYSTEM_CONFIGURATION",
        "--define:FX_NO_THREAD",
        "--define:FX_NO_THREADABORT",
        "--define:FX_NO_WAITONE_MILLISECONDS",
        "--define:FX_NO_WEB_CLIENT",
        "--define:FX_NO_WIN_REGISTRY",
        "--define:FX_NO_WINFORMS",
        "--define:FX_NO_INDENTED_TEXT_WRITER",
        "--define:FX_REDUCED_EXCEPTIONS",
        "--define:FX_RESHAPED_REFEMIT",
        "--define:FX_RESHAPED_GLOBALIZATION",
        "--define:FX_RESHAPED_REFLECTION",
        "--define:FX_RESHAPED_MSBUILD",
        "--define:NETSTANDARD",
        "--define:NETSTANDARD1_6",
        "--optimize-"
    ],
    "fsharp.project.includeCompileBefore": true
}
```

## Working with FSLex/FSYacc

Set `fsharp.project.includeCompileBefore` so that the lexer/parser can access the `<CompileBefore .. />` MSBuild items.

