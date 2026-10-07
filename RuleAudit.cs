namespace LexForge250;

/// <summary>
/// Result of auditing a compiled rule set. Lists are stably ordered.
/// </summary>
public sealed record RuleAuditReport(
    IReadOnlyList<RuleOverlap> Overlaps,
    IReadOnlyList<RuleOutcome> Outcomes);

/// <summary>
/// A pair of rules whose languages share at least one non-empty word,
/// together with the shortest (ASCII-lex-smallest on ties) such word.
/// Only a shared prefix does not count.
/// </summary>
public sealed record RuleOverlap(string FirstRule, string SecondRule, string Witness);

/// <summary>
/// Per-rule verdict in declaration order. A winnable rule carries the
/// shortest full input on which it wins from a fresh scan; a fully
/// shadowed rule carries its shortest acceptable word and the earlier
/// rule that actually wins on that input.
/// </summary>
public sealed record RuleOutcome(
    string RuleName,
    bool CanWin,
    string? Witness,
    string? ShadowWitness,
    string? WinningRule)
{
    internal static RuleOutcome Winnable(string name, string witness)
        => new(name, true, witness, null, null);

    internal static RuleOutcome Shadowed(string name, string shortestWord, string winningRule)
        => new(name, false, null, shortestWord, winningRule);
}

/// <summary>
/// Exact automata reachability analysis. All witnesses are found by a
/// breadth-first search of a product DFA over the ASCII alphabet, which
/// yields the shortest accepting word and, among equal lengths, the
/// ASCII-code lexicographically smallest one. No regexes, sampling or
/// bounded-length enumeration are used.
/// </summary>
internal static class RuleAuditor
{
    private const int MaxProductStates = 4096;

    public static RuleAuditReport Audit(
        string[] names,
        bool[] skip,
        Dfa[] ruleDfas,
        Dfa[] prefixUnionDfas,
        Dfa fullDfa)
    {
        int count = names.Length;

        var overlaps = new List<RuleOverlap>();
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                // (L_i intersect L_j) minus { empty }: both components must
                // be accepting only after at least one character is read.
                string? witness = ProductSearch.Shortest(
                    ruleDfas[i], ruleDfas[j],
                    (a, b, length) => length > 0
                        && ruleDfas[i].AcceptRule[a] >= 0
                        && ruleDfas[j].AcceptRule[b] >= 0);
                if (witness is not null)
                    overlaps.Add(new RuleOverlap(names[i], names[j], witness));
            }
        }

        var outcomes = new List<RuleOutcome>(count);
        for (int i = 0; i < count; i++)
        {
            // A word w makes rule i win as a complete input iff w is in L_i
            // and no earlier rule accepts a strict extension wu (u != ""),
            // which would beat w by longest match. Earlier rules accepting
            // exactly w do not matter: ties are broken by declaration order
            // and later rules cannot beat earlier ones at equal length.
            // CanExtendToAccept(s) holds iff s reaches an accept state of
            // the union DFA through at least one more character.
            string? winner = ProductSearch.Shortest(
                ruleDfas[i], prefixUnionDfas[i],
                (ri, union, length) => length > 0
                    && ruleDfas[i].AcceptRule[ri] >= 0
                    && !prefixUnionDfas[i].CanExtendToAccept(union));

            if (winner is not null)
            {
                outcomes.Add(RuleOutcome.Winnable(names[i], winner));
                continue;
            }

            // Fully shadowed: take its own shortest acceptable word and ask
            // the combined DFA which rule wins on it as a complete input.
            string shortest = ProductSearch.Shortest(
                ruleDfas[i], ruleDfas[i],
                (a, b, length) => length > 0 && ruleDfas[i].AcceptRule[a] >= 0)!
                ?? throw new InvalidOperationException($"Rule '{names[i]}' accepts no word.");

            int winningIndex = WinnerOn(fullDfa, shortest);
            outcomes.Add(RuleOutcome.Shadowed(names[i], shortest, names[winningIndex]));
        }

        return new RuleAuditReport(overlaps, outcomes);
    }

    // Reproduces the scanner's choice when the whole input is consumed:
    // the accept recorded at the final state is the winning rule (longest
    // match, ties broken by declaration order).
    private static int WinnerOn(Dfa dfa, string word)
    {
        int state = 0;
        foreach (char c in word)
        {
            state = dfa.Transitions[state][c];
            if (state < 0)
                throw new InvalidOperationException("Witness left the combined DFA.");
        }
        int rule = dfa.AcceptRule[state];
        return rule < 0
            ? throw new InvalidOperationException("Witness is not accepted by the combined DFA.")
            : rule;
    }

    private static class ProductSearch
    {
        // Breadth-first search over pairs of DFA states. Characters are
        // tried in ASCII order, so the first goal found is the shortest
        // ASCII-lex-smallest witness.
        public static string? Shortest(Dfa first, Dfa second,
            Func<int, int, int, bool> isGoal)
        {
            int n1 = first.Transitions.Length;
            int n2 = second.Transitions.Length;
            if (n1 * (long)n2 > int.MaxValue)
                throw new LexerCompileException("Automaton product exceeded the addressable state count.");

            var visited = new bool[n1 * n2];
            var parent = new Dictionary<int, (int Prev, char Ch)>();
            var queue = new Queue<int>();
            int reached = 1;
            visited[0] = true;
            queue.Enqueue(0);

            var lengthAt = new Dictionary<int, int> { [0] = 0 };

            while (queue.Count > 0)
            {
                int packed = queue.Dequeue();
                int s1 = packed / n2;
                int s2 = packed % n2;
                int length = lengthAt[packed];
                if (isGoal(s1, s2, length))
                    return Reconstruct(parent, packed, n2);

                for (int c = 0; c < 128; c++)
                {
                    int t1 = first.Transitions[s1][c];
                    int t2 = second.Transitions[s2][c];
                    if (t1 < 0 || t2 < 0)
                        continue;
                    int next = t1 * n2 + t2;
                    if (visited[next])
                        continue;
                    if (reached >= MaxProductStates)
                        throw new LexerCompileException(
                            $"Audit product-state limit of {MaxProductStates} exceeded.");
                    reached++;
                    visited[next] = true;
                    parent[next] = (packed, (char)c);
                    lengthAt[next] = length + 1;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        private static string Reconstruct(Dictionary<int, (int Prev, char Ch)> parent, int packed, int n2)
        {
            var chars = new List<char>();
            int cur = packed;
            while (cur != 0)
            {
                var (prev, ch) = parent[cur];
                chars.Add(ch);
                cur = prev;
            }
            chars.Reverse();
            return new string(chars.ToArray());
        }
    }
}
