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
- Re-lint when a parent file is edited
- Autocompleting in strings
- Rename symbol renames whole Path.To.symbol
- Invalidate descendent projects when .fsproj or project.assets.json is changed

# Optimizations
Don't check entire project for rename if symbol is local

# Features
- Docstrings on autocomplete and hover