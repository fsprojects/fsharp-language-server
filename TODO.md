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
Make progress message custom, only 1 progress message

# Bugs
- Autocompleting in strings
- Files in unrelated projects are invalidating open files
- .fsx scripts can contain .fs files via #load directives

# Optimizations
- Use .sln files to decide what .fsproj files to load
- Cancel redundant analyze-.fsproj sequences when .fsproj files get modified repeatedly

# Features
- Docstrings on autocomplete and hover