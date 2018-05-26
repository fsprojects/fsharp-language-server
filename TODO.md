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
- Re-lint files when .fsproj or projects.assets.json changes

# Optimizations
Don't check entire project for rename if symbol is local

# Features
- Docstrings on autocomplete and hover
- Progress bar showing background project check