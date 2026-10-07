# LexForge250

A lexer-rule compilation library for .NET 8 / C# 12. Rules are compiled once into a shared DFA and the resulting lexer can scan any number of texts repeatedly. No `System.Text.RegularExpressions` and no external generator: patterns are parsed by a hand-written recursive-descent parser, compiled to a Thompson NFA, then determinized via subset construction with epsilon closures.

## Public API

```csharp
var lexer = LexerCompiler.Compile(new LexRule[]
{
    new("Keyword", "if|else|while"),
    new("Ident",   "[a-zA-Z_][a-zA-Z0-9_]*"),
    new("Number",  "[0-9]+(\\.[0-9]+)?"),
    new("Ws",      "[ \\t\\n]+", Skip: true),
});
IReadOnlyList<LexToken> tokens = lexer.Scan("if x1\n  42");
```

- `LexRule(Name, Pattern, Skip)` - rules are matched in list order (priority).
- `LexToken(Name, Text, Offset, Length, Line, Column)` - offset/length are 0-based, line/column are 1-based; only LF advances the line.
- `Lexer.Scan(text)` - stateless; safe to reuse across texts and threads. Empty text returns an empty result.
- `LexerCompileException` - carries `RuleName` and `PatternOffset` for syntax errors.
- `LexerScanException` - carries `Offset`, `Line`, `Column` of the first unrecognized position; scanning stops (no character skipping).

## Pattern syntax

- ASCII literals and escapes: `\n`, `\r`, `\t`, and escaped metacharacters (`\\ . * + ? ( ) [ ] | ^ $ { } -`).
- Character classes with ascending ranges: `[a-z0-9_]`.
- Grouping `(...)`, concatenation, alternation `|`, repetition `*` `+` `?`.
- Precedence (high to low): repetition, concatenation, alternation.
- Not supported (compile-time error with rule name and pattern offset): wildcard `.`, negated classes `[^...]`, anchors `^` `$`, counted repetition `{m,n}`, backreferences.
- Rules that can match the empty string are rejected.

## Matching semantics

- Longest match wins at the current position; ties break by rule order.
- On a dead end or end of input the scanner falls back to the last accepting position; remaining characters take part in the next token, nothing is lost.
- `Skip` rules produce no token but still advance position/line/column.
- Non-ASCII input is an error reported at the first offending position.

## Limits

- NFA and DFA are each capped at 4096 states; exceeding either fails compilation with a clear error (no partial lexer is produced).
- Compilation snapshots the rule list; later caller mutations have no effect.

## Layout

- `Abstractions.cs` - public types (`LexRule`, `LexToken`, exceptions).
- `PatternParser.cs` / `Ast.cs` - pattern syntax tree and validation.
- `Nfa.cs` - Thompson construction.
- `Dfa.cs` - subset construction with epsilon closure and accept priorities.
- `Lexer.cs` - `LexerCompiler` and the scanning loop.
- `LexForge250.Tests` - xUnit tests; `LexForge250.Demo` - runnable example.

## Build, test, demo

```sh
dotnet build LexForge250.csproj
dotnet test LexForge250.Tests
dotnet run --project LexForge250.Demo
```
