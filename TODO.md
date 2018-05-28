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

# Bugs
- Autocompleting in strings
- $(TargetFramework) doesn't get substituted in .fsproj files
- When autocomplete is empty, force a check
- Editing doesn't invalidate downstream files

# Optimizations

# Features
- Docstrings on autocomplete and hover