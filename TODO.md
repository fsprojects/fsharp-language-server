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
- $(TargetFramework) doesn't get substituted in .fsproj files

# Optimizations

# Features
- Docstrings on autocomplete and hover