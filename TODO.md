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

# Optimizations
- Cancel redundant analyze-.fsproj sequences when .fsproj files get modified repeatedly
- Only rebuild .fsproj files that depend on the saved one
- Only re-analyze .fsx when # directives are changed

# Features
- When build fails, create default options with incomplete context
- Run-test code lens