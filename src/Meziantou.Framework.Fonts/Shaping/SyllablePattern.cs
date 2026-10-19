using System.Numerics;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// A pattern of syllable categories, for the scripts whose text is cut into syllables before it is shaped: a regular
/// expression whose letters are the categories of the characters.
/// </summary>
/// <remarks>
/// <para>
/// The specifications of these scripts give the syllables as patterns ("a consonant, an optional nukta, any number
/// of halant and consonant, matras, modifiers"). A pattern is written here as it is there: names of categories and of
/// other patterns, one after the other; <c>|</c> between alternatives; <c>?</c>, <c>*</c>, <c>+</c> or
/// <c>{min,max}</c> after what is repeated; parentheses.
/// </para>
/// <para>
/// A pattern is compiled once into a nondeterministic automaton, then into the deterministic automaton that follows
/// all its states at once (the subset construction): a match reads one entry of a table per character, and is the
/// longest one.
/// </para>
/// </remarks>
internal sealed class SyllablePattern
{
    // The categories are the values 0 to 63, the bits of the sets of categories of the names.
    private const int CategoryBits = 6;
    private const int CategoryCount = 1 << CategoryBits;

    // A state of the deterministic automaton is a byte: the patterns of the models have a few dozen states.
    private const int MaxStates = byte.MaxValue + 1;

    // State 0 has no state of the nondeterministic automaton: a match that enters it is over. State 1 is the start.
    private const int StartState = 1;

    // The state a state and a category lead to, at (state << CategoryBits) | category.
    private readonly byte[] _transitions;
    private readonly bool[] _isAccepting;

    private SyllablePattern(byte[] transitions, bool[] isAccepting)
    {
        _transitions = transitions;
        _isAccepting = isAccepting;
    }

    /// <summary>Compiles a pattern.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="categories">The categories a name stands for, as a set of bits: the bit of each category value.</param>
    /// <param name="patterns">The patterns a name stands for.</param>
    public static SyllablePattern Compile(string pattern, IReadOnlyDictionary<string, ulong> categories, IReadOnlyDictionary<string, string> patterns)
    {
        var builder = new Builder(categories, patterns);
        var position = 0;
        var (start, exits) = builder.ParseAlternatives(pattern, ref position, depth: 0);
        if (position != pattern.Length)
            throw new ArgumentException($"The pattern '{pattern}' has an unexpected '{pattern[position]}'", nameof(pattern));

        var accept = builder.AddState(0, -1, -1);
        builder.Connect(exits, accept);
        var (transitions, isAccepting) = builder.ToDeterministic(start, accept, pattern);
        return new SyllablePattern(transitions, isAccepting);
    }

    /// <summary>Finds the longest sequence of glyphs the pattern matches from a glyph.</summary>
    /// <param name="info">The glyphs, with their syllable categories.</param>
    /// <param name="start">The first glyph.</param>
    /// <returns>The number of glyphs of the match; 0 when the pattern does not match, or matches nothing.</returns>
    public int Match(ReadOnlySpan<GlyphInfo> info, int start)
    {
        var transitions = _transitions;
        var isAccepting = _isAccepting;
        var state = StartState;
        var longest = 0;
        for (var i = start; i < info.Length; i++)
        {
            state = transitions[(state << CategoryBits) | (info[i].SyllableCategory & (CategoryCount - 1))];
            if (state == 0)
                break;

            if (isAccepting[state])
            {
                longest = i - start + 1;
            }
        }

        return longest;
    }

    /// <summary>Finds the longest sequence of categories the pattern matches from one of them.</summary>
    /// <param name="categories">The syllable categories of the characters.</param>
    /// <param name="start">The first one.</param>
    /// <returns>The number of categories of the match; 0 when the pattern does not match, or matches nothing.</returns>
    public int Match(ReadOnlySpan<byte> categories, int start)
    {
        var transitions = _transitions;
        var isAccepting = _isAccepting;
        var state = StartState;
        var longest = 0;
        for (var i = start; i < categories.Length; i++)
        {
            state = transitions[(state << CategoryBits) | (categories[i] & (CategoryCount - 1))];
            if (state == 0)
                break;

            if (isAccepting[state])
            {
                longest = i - start + 1;
            }
        }

        return longest;
    }

    private sealed class Builder(IReadOnlyDictionary<string, ulong> categories, IReadOnlyDictionary<string, string> patterns)
    {
        private const int MaxDepth = 16;

        public List<ulong> Categories { get; } = [];

        public List<int> Next { get; } = [];

        public List<int> Alternative { get; } = [];

        public int AddState(ulong category, int next, int alternative)
        {
            Categories.Add(category);
            Next.Add(next);
            Alternative.Add(alternative);
            return Categories.Count - 1;
        }

        // The deterministic automaton of the states from start to accept. Each of its states is a set of states of the
        // nondeterministic one, as bits: the states a match can be in after the same categories, which are those a
        // category leads to, with the states they lead to without reading a character. The first set is the empty one,
        // the second one the start.
        public (byte[] Transitions, bool[] IsAccepting) ToDeterministic(int start, int accept, string pattern)
        {
            var words = (Categories.Count + 63) / 64;
            var sets = new List<ulong[]>();
            var indexes = new Dictionary<ulong[], int>(StateSetComparer.Instance);
            var transitions = new List<byte>();
            AddSet(new ulong[words]);
            var startSet = new ulong[words];
            AddClosure(startSet, start);
            AddSet(startSet);

            // The sets are numbered in the order they are found, and their transitions added in that order.
            for (var index = 0; index < sets.Count; index++)
            {
                var set = sets[index];
                for (var category = 0; category < CategoryCount; category++)
                {
                    var target = new ulong[words];
                    for (var word = 0; word < words; word++)
                    {
                        for (var bits = set[word]; bits != 0; bits &= bits - 1)
                        {
                            var state = (word * 64) + BitOperations.TrailingZeroCount(bits);
                            if ((Categories[state] & (1UL << category)) != 0)
                            {
                                AddClosure(target, Next[state]);
                            }
                        }
                    }

                    if (!indexes.TryGetValue(target, out var targetIndex))
                    {
                        targetIndex = AddSet(target);
                    }

                    transitions.Add((byte)targetIndex);
                }
            }

            var isAccepting = new bool[sets.Count];
            for (var index = 0; index < sets.Count; index++)
            {
                isAccepting[index] = (sets[index][accept / 64] & (1UL << (accept % 64))) != 0;
            }

            return ([.. transitions], isAccepting);

            int AddSet(ulong[] set)
            {
                if (sets.Count == MaxStates)
                    throw new ArgumentException($"The pattern '{pattern}' needs more than {MaxStates} states", nameof(pattern));

                indexes.Add(set, sets.Count);
                sets.Add(set);
                return sets.Count - 1;
            }
        }

        // Adds a state and the states it leads to without reading a character.
        private void AddClosure(ulong[] set, int state)
        {
            while (state >= 0 && (set[state / 64] & (1UL << (state % 64))) == 0)
            {
                set[state / 64] |= 1UL << (state % 64);
                if (Categories[state] != 0)
                    return;

                if (Alternative[state] >= 0)
                {
                    AddClosure(set, Alternative[state]);
                }

                state = Next[state];
            }
        }

        // An exit is a successor that is not set yet: the state, and which of its two successors.
        public void Connect(List<(int State, bool IsAlternative)> exits, int target)
        {
            foreach (var (state, isAlternative) in exits)
            {
                if (isAlternative)
                {
                    Alternative[state] = target;
                }
                else
                {
                    Next[state] = target;
                }
            }
        }

        public (int Start, List<(int State, bool IsAlternative)> Exits) ParseAlternatives(string pattern, ref int position, int depth)
        {
            var (start, exits) = ParseSequence(pattern, ref position, depth);
            while (position < pattern.Length && pattern[position] == '|')
            {
                position++;
                var (other, otherExits) = ParseSequence(pattern, ref position, depth);
                start = AddState(0, start, other);
                exits.AddRange(otherExits);
            }

            return (start, exits);
        }

        private (int Start, List<(int State, bool IsAlternative)> Exits) ParseSequence(string pattern, ref int position, int depth)
        {
            // An empty sequence: a state that reads nothing.
            var start = AddState(0, -1, -1);
            var exits = new List<(int State, bool IsAlternative)> { (start, false) };
            while (true)
            {
                while (position < pattern.Length && pattern[position] is ' ' or '.')
                {
                    position++;
                }

                if (position >= pattern.Length || pattern[position] is '|' or ')')
                    return (start, exits);

                var itemStart = position;
                SkipItem(pattern, ref position);
                var item = pattern[itemStart..position];
                var min = 1;
                var max = 1;
                while (position < pattern.Length && pattern[position] is '?' or '*' or '+' or '{')
                {
                    switch (pattern[position++])
                    {
                        case '?':
                            (min, max) = (0, 1);
                            break;

                        case '*':
                            (min, max) = (0, -1);
                            break;

                        case '+':
                            (min, max) = (1, -1);
                            break;

                        default:
                            var close = pattern.AsSpan(position).IndexOf('}') + position;
                            var bounds = pattern[position..close].Split(',');
                            min = int.Parse(bounds[0], CultureInfo.InvariantCulture);
                            max = int.Parse(bounds[^1], CultureInfo.InvariantCulture);
                            position = close + 1;
                            break;
                    }
                }

                // The item, as many times as it must be there; then the times it may be there, each one skippable.
                for (var i = 0; i < min; i++)
                {
                    var (itemState, itemExits) = ParseItem(item, depth);
                    Connect(exits, itemState);
                    exits = itemExits;
                }

                if (max < 0)
                {
                    var (itemState, itemExits) = ParseItem(item, depth);
                    var loop = AddState(0, -1, itemState);
                    Connect(exits, loop);
                    Connect(itemExits, loop);
                    exits = [(loop, false)];
                }
                else
                {
                    for (var i = min; i < max; i++)
                    {
                        var (itemState, itemExits) = ParseItem(item, depth);
                        var skip = AddState(0, -1, itemState);
                        Connect(exits, skip);
                        exits = itemExits;
                        exits.Add((skip, false));
                    }
                }
            }
        }

        private static void SkipItem(string pattern, ref int position)
        {
            if (pattern[position] == '(')
            {
                var depth = 0;
                do
                {
                    depth += pattern[position] == '(' ? 1 : pattern[position] == ')' ? -1 : 0;
                    position++;
                }
                while (depth > 0 && position < pattern.Length);

                return;
            }

            while (position < pattern.Length && (char.IsAsciiLetterOrDigit(pattern[position]) || pattern[position] == '_'))
            {
                position++;
            }
        }

        private (int Start, List<(int State, bool IsAlternative)> Exits) ParseItem(string item, int depth)
        {
            if (depth > MaxDepth)
                throw new ArgumentException("The patterns refer to one another without end", nameof(item));

            if (item[0] == '(')
            {
                var inner = item[1..^1];
                var position = 0;
                return ParseAlternatives(inner, ref position, depth + 1);
            }

            if (categories.TryGetValue(item, out var category))
            {
                var state = AddState(category, -1, -1);
                return (state, [(state, false)]);
            }

            if (patterns.TryGetValue(item, out var pattern))
            {
                var position = 0;
                return ParseAlternatives(pattern, ref position, depth + 1);
            }

            throw new ArgumentException($"The pattern refers to '{item}', which is not defined", nameof(item));
        }
    }

    // Sets of states compare by their bits.
    private sealed class StateSetComparer : IEqualityComparer<ulong[]>
    {
        public static StateSetComparer Instance { get; } = new();

        public bool Equals(ulong[]? x, ulong[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(ulong[] obj)
        {
            var hash = new HashCode();
            foreach (var word in obj)
            {
                hash.Add(word);
            }

            return hash.ToHashCode();
        }
    }
}
