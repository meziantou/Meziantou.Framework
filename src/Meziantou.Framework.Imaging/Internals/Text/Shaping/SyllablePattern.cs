namespace Meziantou.Framework.Imaging.Internals;

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
/// A pattern is compiled once into a nondeterministic automaton and matched by following all its states at once: the
/// time is proportional to the length of the syllable, whatever the text is, and the match is the longest one.
/// </para>
/// </remarks>
internal sealed class SyllablePattern
{
    // The largest automaton whose working storage is on the stack: the patterns of the models are well below.
    private const int MaxStackStates = 4096;

    // A state has a set of categories and one successor, or no category and up to two successors it leads to
    // without reading a character. The last state is the accepting one.
    private readonly ulong[] _categories;
    private readonly int[] _next;
    private readonly int[] _alternative;
    private readonly int _start;
    private readonly int _accept;

    private SyllablePattern(List<ulong> categories, List<int> next, List<int> alternative, int start, int accept)
    {
        _categories = [.. categories];
        _next = [.. next];
        _alternative = [.. alternative];
        _start = start;
        _accept = accept;
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
        return new SyllablePattern(builder.Categories, builder.Next, builder.Alternative, start, accept);
    }

    /// <summary>Finds the longest sequence of glyphs the pattern matches from a glyph.</summary>
    /// <param name="info">The glyphs, with their syllable categories.</param>
    /// <param name="start">The first glyph.</param>
    /// <param name="states">Working storage: at least <see cref="StateCount"/> entries, twice.</param>
    /// <returns>The number of glyphs of the match; 0 when the pattern does not match, or matches nothing.</returns>
    public int Match(ReadOnlySpan<GlyphInfo> info, int start, Span<int> states) => Match(info, default, info.Length, start, states);

    /// <summary>Finds the longest sequence of categories the pattern matches from one of them.</summary>
    /// <param name="categories">The syllable categories of the characters.</param>
    /// <param name="start">The first one.</param>
    /// <param name="states">Working storage: at least <see cref="StateCount"/> entries, twice.</param>
    public int Match(ReadOnlySpan<byte> categories, int start, Span<int> states) => Match(default, categories, categories.Length, start, states);

    private int Match(ReadOnlySpan<GlyphInfo> info, ReadOnlySpan<byte> categories, int length, int start, Span<int> states)
    {
        var count = _categories.Length;
        var current = states[..count];
        var following = states.Slice(count, count);
        Span<bool> isActive = count <= MaxStackStates ? stackalloc bool[count] : new bool[count];
        isActive.Clear();
        var currentCount = 0;
        Add(_start, current, ref currentCount, isActive);
        var longest = 0;
        for (var i = start; ; i++)
        {
            var isAccepted = false;
            for (var j = 0; j < currentCount; j++)
            {
                isAccepted |= current[j] == _accept;
            }

            if (isAccepted)
            {
                longest = i - start;
            }

            if (i >= length || currentCount == 0)
                break;

            var category = 1UL << ((categories.IsEmpty ? info[i].SyllableCategory : categories[i]) & 63);
            isActive.Clear();
            var followingCount = 0;
            for (var j = 0; j < currentCount; j++)
            {
                var state = current[j];
                if ((_categories[state] & category) != 0)
                {
                    Add(_next[state], following, ref followingCount, isActive);
                }
            }

            var swap = current;
            current = following;
            following = swap;
            currentCount = followingCount;
        }

        return longest;
    }

    /// <summary>Gets the number of states of the automaton.</summary>
    public int StateCount => _categories.Length;

    // Adds a state and the states it leads to without reading a character.
    private void Add(int state, Span<int> states, ref int count, Span<bool> isActive)
    {
        while (state >= 0 && !isActive[state])
        {
            isActive[state] = true;
            states[count++] = state;
            if (_categories[state] != 0 || state == _accept)
                return;

            if (_alternative[state] >= 0)
            {
                Add(_alternative[state], states, ref count, isActive);
            }

            state = _next[state];
        }
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
}
