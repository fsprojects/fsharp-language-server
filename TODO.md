# Highlighting
- UnionConstructor { ... } is highlighted like seq { ... }
- ``some-name`` is highlighted wrong at -
- ``name`` is highlighted like a function name regardless of context
- "string format %d" doesn't format %d
- @"\w" doesn't highlight regexes
- Missing keywords:
  - to
  - downto
  - type
  - not
  - done

# Cleanup

# Bugs
- Autocompleting in strings and comments
- Crack FCS
- Reload options when .fsx is saved
- Invalidate check results when referenced .dlls are modified
- Projects targeting netstandard2.0 show fake errors
- Save upstream file not triggering re-lint
- Unused-open is sometimes wrong??? See ProgressBar.fs
- Concurrency errors; use a single thread for everything except FSharpCompilerService ops

# Optimizations
- Cancel redundant analyze-.fsproj sequences when .fsproj files get modified repeatedly
- Only rebuild .fsproj files that depend on the saved one
- Only re-analyze .fsx when # directives are changed
- Only show progress bars once 1s has passed
- Try erasing source after cursor to speed up incremental re-compilation
- Use timer to decide when to use stale results? https://github.com/fsharp/FSharp.Compiler.Service/blob/62098efc35fe24f7e6824b89e47ff1eb031d55a5/vsintegration/src/FSharp.Editor/LanguageService/FSharpCheckerExtensions.fs
- Check out get-project-options implementation in https://github.com/fsharp/FSharp.Compiler.Service/blob/62098efc35fe24f7e6824b89e47ff1eb031d55a5/vsintegration/src/FSharp.Editor/LanguageService/ProjectSitesAndFiles.fs
- Check out Omnisharp project loading OmniSharp.MSBuild.ProjectLoader

# Features
- Allow emitting obj/FscArgs.txt as a project-cracker backup
- fsharp.task.test problem-matchers
- Emit .trx files from tests, and use them to highlight failed tests
- Show "you need to press play" popup the first time the user debugs something
- Popup to do restore, like C#