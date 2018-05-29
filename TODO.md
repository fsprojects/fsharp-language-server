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
- Convert Uri to FileInfo as early as possible

# Bugs
- Autocompleting in strings
- Files in unrelated projects are invalidating open files

# Optimizations
- Use .sln files to decide what .fsproj files to load
- Cancel redundant analyze-.fsproj sequences when .fsproj files get modified repeatedly

# Features
- Docstrings on autocomplete and hover