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
- Set --framework in test command
- Signature code lenses disappear when there are parse errors
- Hover is off-by-one; you need to hover slightly to the right to get the popup

# Optimizations
- Add analyze-incrementally operation to F# compiler
- When .fsi files are present, changing corresponding .fs file doesn't invalidate anything

# Features
- Allow emitting obj/FscArgs.txt as a project-cracker backup
- fsharp.task.test problem-matchers
- Emit .trx files from tests, and use them to highlight failed tests
- Show "you need to press play" popup the first time the user debugs something
- Popup to do restore, like C#