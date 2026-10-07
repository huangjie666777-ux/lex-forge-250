namespace LexForge250;

/// <summary>Shared DFA built by subset construction with epsilon closure.</summary>
internal sealed class Dfa
{
    public const int MaxStates = 4096;
    private const int Alphabet = 128;

    public readonly int[][] Transitions; // [state][char] -> state, -1 = dead
    public readonly int[] AcceptRule;    // best (lowest-index) rule per state, -1 = none
    // States from which some non-empty word reaches an accepting state.
    private readonly bool[] _canExtendToAccept;

    private Dfa(int[][] transitions, int[] acceptRule, bool[] canExtendToAccept)
    {
        Transitions = transitions;
        AcceptRule = acceptRule;
        _canExtendToAccept = canExtendToAccept;
    }

    /// <summary>
    /// True iff a non-empty continuation from the state reaches an accepting
    /// state, i.e. an earlier rule accepts the current prefix extended by
    /// at least one more character.
    /// </summary>
    public bool CanExtendToAccept(int state) => _canExtendToAccept[state];

    /// <summary>A two-state DFA accepting no word and never reaching accept.</summary>
    public static Dfa LiveNonAccepting()
    {
        var start = new int[Alphabet];
        var loop = new int[Alphabet];
        Array.Fill(start, 1);
        Array.Fill(loop, 1);
        return new Dfa(new[] { start, loop }, new[] { -1, -1 }, new[] { false, false });
    }

    public static Dfa Build(Nfa nfa)
    {
        var closureCache = new Dictionary<Nfa.State, List<Nfa.State>>();
        var dfaStates = new List<HashSet<Nfa.State>>();
        var ids = new Dictionary<HashSet<Nfa.State>, int>(HashSet<Nfa.State>.CreateSetComparer());
        var transitions = new List<int[]>();
        var accept = new List<int>();

        int AddState(HashSet<Nfa.State> set)
        {
            if (ids.TryGetValue(set, out int existing))
                return existing;
            if (dfaStates.Count >= MaxStates)
                throw new LexerCompileException($"DFA state limit of {MaxStates} exceeded.");
            int id = dfaStates.Count;
            dfaStates.Add(set);
            ids.Add(set, id);
            transitions.Add(new int[Alphabet]);
            Array.Fill(transitions[id], -1);
            int best = -1;
            foreach (Nfa.State s in set)
                if (s.AcceptRule >= 0 && (best < 0 || s.AcceptRule < best))
                    best = s.AcceptRule;
            accept.Add(best);
            return id;
        }

        AddState(EpsilonClosure(new[] { nfa.Start }, closureCache));
        for (int i = 0; i < dfaStates.Count; i++)
        {
            // Collect per-character moves by expanding ranges.
            var moves = new Dictionary<int, HashSet<Nfa.State>>();
            foreach (Nfa.State s in dfaStates[i])
            {
                foreach (var (lo, hi, target) in s.Transitions)
                {
                    for (int c = lo; c <= hi; c++)
                    {
                        if (!moves.TryGetValue(c, out var bucket))
                            moves[c] = bucket = new HashSet<Nfa.State>();
                        bucket.Add(target);
                    }
                }
            }
            foreach (var (c, targets) in moves)
                transitions[i][c] = AddState(EpsilonClosure(targets, closureCache));
        }
        int[][] table = transitions.ToArray();
        int[] accepts = accept.ToArray();
        return new Dfa(table, accepts, ComputeExtendable(table, accepts));
    }

    // Reverse reachability over at least one transition: a state qualifies
    // if it moves directly to an accepting state or to a qualifying state.
    private static bool[] ComputeExtendable(int[][] transitions, int[] acceptRule)
    {
        int n = transitions.Length;
        var result = new bool[n];
        var reverse = new List<int>[n];
        for (int s = 0; s < n; s++)
            reverse[s] = new List<int>();
        var queue = new Queue<int>();
        for (int s = 0; s < n; s++)
        {
            for (int c = 0; c < Alphabet; c++)
            {
                int t = transitions[s][c];
                if (t < 0)
                    continue;
                reverse[t].Add(s);
                if (!result[s] && acceptRule[t] >= 0)
                {
                    result[s] = true;
                    queue.Enqueue(s);
                }
            }
        }
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            foreach (int s in reverse[t])
            {
                if (!result[s])
                {
                    result[s] = true;
                    queue.Enqueue(s);
                }
            }
        }
        return result;
    }

    private static HashSet<Nfa.State> EpsilonClosure(IEnumerable<Nfa.State> seeds,
        Dictionary<Nfa.State, List<Nfa.State>> cache)
    {
        var result = new HashSet<Nfa.State>();
        var stack = new Stack<Nfa.State>();
        foreach (Nfa.State s in seeds)
        {
            if (!cache.TryGetValue(s, out var memo))
            {
                memo = ComputeClosure(s);
                cache[s] = memo;
            }
            foreach (Nfa.State m in memo)
                if (result.Add(m))
                    stack.Push(m);
        }
        return result;
    }

    private static List<Nfa.State> ComputeClosure(Nfa.State seed)
    {
        var list = new List<Nfa.State>();
        var seen = new HashSet<Nfa.State>();
        var stack = new Stack<Nfa.State>();
        stack.Push(seed);
        while (stack.Count > 0)
        {
            Nfa.State s = stack.Pop();
            if (!seen.Add(s))
                continue;
            list.Add(s);
            foreach (Nfa.State e in s.Epsilon)
                stack.Push(e);
        }
        return list;
    }
}
