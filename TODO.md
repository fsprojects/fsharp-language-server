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
- Don't autocomplete things with spaces
- Crack FCS
- Reload options when .fsx is saved

# Optimizations
- Cancel redundant analyze-.fsproj sequences when .fsproj files get modified repeatedly
- Only rebuild .fsproj files that depend on the saved one
- Only re-analyze .fsx when # directives are changed
- Analyze project.assets.json lazily
- Only show progress bars once 1s has passed
- Try erasing source after cursor to speed up incremental re-compilation

# Features
- When build fails, create default options with incomplete context
- Run-test code lens
- Source dep on C# project
- Allow emitting obj/FscArgs.txt as a project-cracker backup