namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// Finds the longest label that starts at each position of a text, in a time linear in the length of the text whatever the
/// number of labels. It is an Aho-Corasick automaton of the reversed labels, which reads the text backward: the labels it finds
/// end where they start in the text.
/// </summary>
internal sealed class AbbreviationMatcher
{
    private readonly Dictionary<(int State, char Character), int> _transitions = [];

    // For each state: the state of the longest proper suffix of its string that is a state, and the longest label that is a
    // suffix of its string
    private readonly List<int> _failures = [0];
    private readonly List<string?> _longestLabels = [null];

    public AbbreviationMatcher(IEnumerable<string> labels)
    {
        var children = new List<List<(char Character, int State)>> { new() };
        foreach (var label in labels)
        {
            if (label.Length == 0)
            {
                continue;
            }

            var state = 0;
            for (var i = label.Length - 1; i >= 0; i--)
            {
                if (!_transitions.TryGetValue((state, label[i]), out var next))
                {
                    next = _failures.Count;
                    _failures.Add(0);
                    _longestLabels.Add(null);
                    children.Add([]);
                    children[state].Add((label[i], next));
                    _transitions.Add((state, label[i]), next);
                }

                state = next;
            }

            _longestLabels[state] = label;
        }

        // The states by depth, so that the state of a suffix is complete before the states that use it
        var queue = new Queue<int>();
        foreach (var (_, state) in children[0])
        {
            queue.Enqueue(state);
        }

        while (queue.TryDequeue(out var parent))
        {
            foreach (var (character, state) in children[parent])
            {
                var failure = _failures[parent];
                while (failure != 0 && !_transitions.ContainsKey((failure, character)))
                {
                    failure = _failures[failure];
                }

                if (parent != 0 && _transitions.TryGetValue((failure, character), out var next))
                {
                    failure = next;
                }

                _failures[state] = failure;
                _longestLabels[state] ??= _longestLabels[failure];
                queue.Enqueue(state);
            }
        }
    }

    /// <summary>Writes the longest label that starts at each position of the text, or <see langword="null"/> when none does.</summary>
    public void FindLongestLabels(ReadOnlySpan<char> text, Span<string?> labels)
    {
        var state = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = text[i];
            while (true)
            {
                if (_transitions.TryGetValue((state, c), out var next))
                {
                    state = next;
                    break;
                }

                if (state == 0)
                {
                    break;
                }

                state = _failures[state];
            }

            labels[i] = _longestLabels[state];
        }
    }
}
