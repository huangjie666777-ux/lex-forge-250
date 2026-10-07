namespace LexForge250;

/// <summary>
/// A compiled, immutable lexer. Safe to reuse across threads and texts;
/// no scanning state is retained between calls.
/// </summary>
public sealed class Lexer
{
    private readonly Dfa _dfa;
    private readonly string[] _names;
    private readonly bool[] _skip;
    private readonly Dfa[] _ruleDfas;
    private readonly Dfa[] _prefixUnionDfas;

    internal Lexer(Dfa dfa, Dfa[] ruleDfas, Dfa[] prefixUnionDfas, string[] names, bool[] skip)
    {
        _dfa = dfa;
        _names = names;
        _skip = skip;
        _ruleDfas = ruleDfas;
        _prefixUnionDfas = prefixUnionDfas;
    }

    /// <summary>Tokenizes the entire text. Empty text yields an empty result.</summary>
    public IReadOnlyList<LexToken> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<LexToken>();
        int pos = 0, line = 1, col = 1;
        while (pos < text.Length)
        {
            int state = 0;
            int lastAcceptPos = -1, lastAcceptRule = -1;
            int cursor = pos;
            while (cursor < text.Length)
            {
                char c = text[cursor];
                if (c > 0x7F)
                    break; // handled below if no accept was reached
                int next = _dfa.Transitions[state][c];
                if (next < 0)
                    break;
                state = next;
                cursor++;
                int rule = _dfa.AcceptRule[state];
                if (rule >= 0)
                {
                    lastAcceptPos = cursor;
                    lastAcceptRule = rule;
                }
            }
            if (lastAcceptPos < 0)
            {
                char bad = text[pos];
                string why = bad > 0x7F
                    ? $"Non-ASCII character U+{(int)bad:X4}"
                    : $"Unrecognized character '{bad}'";
                throw new LexerScanException($"{why} at offset {pos} (line {line}, column {col}).", pos, line, col);
            }
            string lexeme = text.Substring(pos, lastAcceptPos - pos);
            if (!_skip[lastAcceptRule])
                tokens.Add(new LexToken(_names[lastAcceptRule], lexeme, pos, lexeme.Length, line, col));
            foreach (char c in lexeme)
            {
                if (c == '\n') { line++; col = 1; }
                else col++;
            }
            pos = lastAcceptPos;
        }
        return tokens;
    }

    /// <summary>
    /// Audits the compiled rule set: reports every pair of rules whose
    /// languages intersect (with a shortest witness accepted by both), and
    /// decides per rule whether it can ever win against its preceding rules
    /// or is completely shadowed by their union. The result is computed
    /// from the compiled automata and never mutates scanner state.
    /// </summary>
    public RuleAuditReport Audit() => RuleAuditor.Audit(_names, _skip, _ruleDfas, _prefixUnionDfas, _dfa);
}

/// <summary>Compiles prioritized rules into a reusable <see cref="Lexer"/>.</summary>
public static class LexerCompiler
{
    public static Lexer Compile(IReadOnlyList<LexRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0)
            throw new LexerCompileException("At least one rule is required.");

        // Snapshot so later caller mutations cannot affect the compiled lexer.
        var snapshot = rules.ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fullNfa = new Nfa();
        var names = new string[snapshot.Length];
        var skip = new bool[snapshot.Length];
        var asts = new Node[snapshot.Length];

        for (int i = 0; i < snapshot.Length; i++)
        {
            LexRule rule = snapshot[i] ?? throw new LexerCompileException($"Rule at index {i} is null.");
            if (string.IsNullOrEmpty(rule.Name))
                throw new LexerCompileException($"Rule at index {i} has an empty name.");
            if (!seen.Add(rule.Name))
                throw new LexerCompileException($"Duplicate rule name '{rule.Name}'.", rule.Name);
            if (rule.Pattern is null)
                throw new LexerCompileException($"Rule '{rule.Name}' has a null pattern.", rule.Name);

            Node ast = PatternParser.Parse(rule.Pattern, rule.Name);
            if (Node.IsNullable(ast))
                throw new LexerCompileException($"Rule '{rule.Name}' can match the empty string.", rule.Name);
            asts[i] = ast;
            fullNfa.AddRule(ast, i);
            names[i] = rule.Name;
            skip[i] = rule.Skip;
        }

        Dfa dfa = Dfa.Build(fullNfa);

        // Per-rule automata keep exact per-language information for the audit.
        var ruleDfas = new Dfa[snapshot.Length];
        for (int i = 0; i < snapshot.Length; i++)
        {
            var nfa = new Nfa();
            nfa.AddRule(asts[i], 0);
            ruleDfas[i] = Dfa.Build(nfa);
        }

        // prefixUnionDfas[i] accepts the union of rules 0..i-1 (empty for i == 0).
        // The audit uses per-state strict-extension reachability of these DFAs.
        var prefixUnionDfas = new Dfa[snapshot.Length];
        var prefixNfa = new Nfa();
        for (int i = 0; i < snapshot.Length; i++)
        {
            prefixUnionDfas[i] = i == 0 ? Dfa.LiveNonAccepting() : Dfa.Build(prefixNfa);
            prefixNfa.AddRule(asts[i], i);
        }

        return new Lexer(dfa, ruleDfas, prefixUnionDfas, names, skip);
    }
}
