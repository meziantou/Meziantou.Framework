using System.Collections;

namespace Meziantou.Framework.Collections;

/// <summary>
/// Represents a case-sensitive radix-compressed trie optimized for ranked prefix autocomplete.
/// </summary>
/// <remarks>
/// <para>
/// This collection stores terms with a frequency score and can efficiently retrieve the top-k completions for a prefix.
/// It augments each node with the maximum frequency in its subtree so low-ranking branches can be pruned during lookup.
/// </para>
/// <para>Typical use cases include search-box suggestions, query completion, and dictionary-backed command palettes.</para>
/// <para>The structure is case-sensitive. If you need case-insensitive behavior, normalize input before adding or querying.</para>
/// <para>Example:</para>
/// <code>
/// var tree = new PruningRadixTree();
/// tree.Add("car", 10);
/// tree.Add("cart", 5);
/// tree.Add("cat", 7);
///
/// var suggestions = tree.GetTopTermsByPrefix("ca", topK: 2, out _);
/// // suggestions: [("car", 10), ("cat", 7)]
/// </code>
/// </remarks>
public sealed class PruningRadixTree : IEnumerable<KeyValuePair<string, long>>
{
    private Node _root = new();

    /// <summary>
    /// Initializes a new instance of <see cref="PruningRadixTree"/>.
    /// </summary>
    public PruningRadixTree()
    {
    }

    /// <summary>
    /// Gets the number of stored terms.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Adds a term and its frequency to the tree.
    /// </summary>
    /// <param name="term">The term to add.</param>
    /// <param name="frequency">The positive frequency value to add to the term.</param>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frequency"/> is less than or equal to 0.</exception>
    /// <remarks>
    /// If the term already exists, <paramref name="frequency"/> is added to the existing value.
    /// </remarks>
    public void Add(string term, long frequency)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);

        AddCore(_root, term, term, frequency);
    }

    /// <summary>
    /// Adds a sequence of terms and frequencies to the tree.
    /// </summary>
    /// <param name="terms">The terms to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="terms"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException">One of the term keys is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">One of the frequencies is less than or equal to 0.</exception>
    /// <remarks>
    /// This method is optimized for bulk loading by aggregating duplicate keys and building the radix tree from sorted entries.
    /// </remarks>
    public void AddRange(IEnumerable<KeyValuePair<string, long>> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        var additions = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (term.Key is null)
                throw new ArgumentException("The sequence contains a null key.", nameof(terms));

            if (term.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(terms), "The sequence contains a non-positive frequency.");

            if (additions.TryGetValue(term.Key, out var currentFrequency))
            {
                additions[term.Key] = currentFrequency + term.Value;
            }
            else
            {
                additions.Add(term.Key, term.Value);
            }
        }

        if (additions.Count is 0)
            return;

        if (Count is 0)
        {
            RebuildTree(additions);
            return;
        }

        var merged = new Dictionary<string, long>(Count + additions.Count, StringComparer.Ordinal);
        foreach (var entry in this)
        {
            merged.Add(entry.Key, entry.Value);
        }

        foreach (var addition in additions)
        {
            if (merged.TryGetValue(addition.Key, out var currentFrequency))
            {
                merged[addition.Key] = currentFrequency + addition.Value;
            }
            else
            {
                merged.Add(addition.Key, addition.Value);
            }
        }

        RebuildTree(merged);
    }

    /// <summary>
    /// Removes a term from the tree.
    /// </summary>
    /// <param name="term">The term to remove.</param>
    /// <returns><see langword="true"/> when the term existed and was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(ReadOnlySpan<char> term)
    {
        if (!RemoveCore(_root, term))
            return false;

        Count--;
        return true;
    }

    /// <summary>
    /// Gets the frequency associated with a term.
    /// </summary>
    /// <param name="term">The term to find.</param>
    /// <param name="frequency">When this method returns, contains the associated frequency if found; otherwise, 0.</param>
    /// <returns><see langword="true"/> if the term exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(ReadOnlySpan<char> term, out long frequency)
    {
        if (TryGetExactNode(term, out var node) && node.TermFrequency > 0)
        {
            frequency = node.TermFrequency;
            return true;
        }

        frequency = 0;
        return false;
    }

    /// <summary>
    /// Determines whether the tree contains a specific term.
    /// </summary>
    /// <param name="term">The term to locate.</param>
    /// <returns><see langword="true"/> if the term exists; otherwise, <see langword="false"/>.</returns>
    public bool ContainsKey(ReadOnlySpan<char> term)
    {
        return TryGetValue(term, out _);
    }

    /// <summary>
    /// Gets the top terms for a given prefix, sorted by frequency descending.
    /// </summary>
    /// <param name="prefix">The prefix to search.</param>
    /// <param name="topK">The maximum number of suggestions to return.</param>
    /// <param name="prefixFrequency">
    /// When this method returns, contains the exact frequency of <paramref name="prefix"/> if it is a stored term; otherwise, 0.
    /// </param>
    /// <param name="pruning">
    /// <see langword="true"/> to prune non-promising branches using subtree metadata; otherwise, <see langword="false"/>.
    /// </param>
    /// <returns>A ranked list of matching terms.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="topK"/> is negative.</exception>
    public IReadOnlyList<KeyValuePair<string, long>> GetTopTermsByPrefix(ReadOnlySpan<char> prefix, int topK, out long prefixFrequency, bool pruning = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(topK);

        if (!TryGetPrefixNode(prefix, out var node, out prefixFrequency) || topK is 0)
            return [];

        var results = new List<KeyValuePair<string, long>>(Math.Min(topK, 16));
        CollectTopTerms(node, topK, pruning, results);
        return results;
    }

    /// <summary>
    /// Returns an enumerator that iterates through all terms in the tree.
    /// </summary>
    /// <returns>An enumerator for all stored terms and frequencies.</returns>
    public IEnumerator<KeyValuePair<string, long>> GetEnumerator() => EnumerateFrom(_root).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void RebuildTree(Dictionary<string, long> terms)
    {
        if (terms.Count is 0)
        {
            _root = new Node();
            Count = 0;
            return;
        }

        var sortedTerms = new TermFrequency[terms.Count];
        var index = 0;
        foreach (var term in terms)
        {
            sortedTerms[index] = new TermFrequency(term.Key, term.Value);
            index++;
        }

        Array.Sort(sortedTerms, static (left, right) => string.CompareOrdinal(left.Term, right.Term));
        _root = BuildNode(sortedTerms, start: 0, end: sortedTerms.Length, depth: 0);
        Count = sortedTerms.Length;
    }

    private static Node BuildNode(TermFrequency[] terms, int start, int end, int depth)
    {
        var node = new Node();
        var index = start;
        if (index < end && terms[index].Term.Length == depth)
        {
            node.Term = terms[index].Term;
            node.TermFrequency = terms[index].Frequency;
            index++;
        }

        if (index < end)
        {
            var children = new List<Edge>();
            while (index < end)
            {
                var childStart = index;
                var currentChar = terms[index].Term[depth];
                index++;
                while (index < end)
                {
                    var term = terms[index].Term;
                    if (term.Length == depth || term[depth] != currentChar)
                        break;

                    index++;
                }

                var childEnd = index;
                var firstSpan = terms[childStart].Term.AsSpan(depth);
                var lastSpan = terms[childEnd - 1].Term.AsSpan(depth);
                var commonPrefixLength = StringSearchUtilities.GetCommonPrefixLength(firstSpan, lastSpan);
                var label = terms[childStart].Term.Substring(depth, commonPrefixLength);
                var child = BuildNode(terms, childStart, childEnd, depth + commonPrefixLength);
                children.Add(new Edge(label, child));
            }

            node.Children = children;
        }

        RecalculateMetadata(node);
        return node;
    }

    private void AddCore(Node node, ReadOnlySpan<char> term, string storedTerm, long frequency)
    {
        if (term.Length is 0)
        {
            if (node.TermFrequency is 0)
            {
                Count++;
                node.Term = storedTerm;
            }

            node.TermFrequency += frequency;
            RecalculateMetadata(node);
            return;
        }

        node.Children ??= [];

        var childIndex = FindChildIndex(node, term[0]);
        if (childIndex is -1)
        {
            var child = new Node
            {
                Term = storedTerm,
                TermFrequency = frequency,
                MaxSubtreeFrequency = frequency,
            };

            node.Children.Add(new Edge(term.ToString(), child));
            Count++;
            RecalculateMetadata(node);
            return;
        }

        var edge = node.Children[childIndex];
        var commonPrefixLength = StringSearchUtilities.GetCommonPrefixLength(term, edge.Label);

        if (commonPrefixLength == edge.Label.Length)
        {
            AddCore(edge.Child, term[commonPrefixLength..], storedTerm, frequency);
            RecalculateMetadata(node);
            return;
        }

        if (commonPrefixLength == term.Length)
        {
            var splitNode = new Node
            {
                Term = storedTerm,
                TermFrequency = frequency,
            };

            splitNode.Children = [new Edge(edge.Label[commonPrefixLength..], edge.Child)];
            Count++;
            RecalculateMetadata(splitNode);
            node.Children[childIndex] = new Edge(term.ToString(), splitNode);
            RecalculateMetadata(node);
            return;
        }

        var branchNode = new Node
        {
            Children =
            [
                new Edge(edge.Label[commonPrefixLength..], edge.Child),
                new Edge(term[commonPrefixLength..].ToString(), new Node
                {
                    Term = storedTerm,
                    TermFrequency = frequency,
                    MaxSubtreeFrequency = frequency,
                }),
            ],
        };

        Count++;
        RecalculateMetadata(branchNode);
        node.Children[childIndex] = new Edge(edge.Label[..commonPrefixLength], branchNode);
        RecalculateMetadata(node);
    }

    private static bool RemoveCore(Node node, ReadOnlySpan<char> term)
    {
        if (term.Length is 0)
        {
            if (node.TermFrequency is 0)
                return false;

            node.TermFrequency = 0;
            node.Term = null;
            RecalculateMetadata(node);
            return true;
        }

        if (node.Children is null)
            return false;

        var childIndex = FindChildIndex(node, term[0]);
        if (childIndex is -1)
            return false;

        var edge = node.Children[childIndex];
        if (!term.StartsWith(edge.Label, StringComparison.Ordinal))
            return false;

        var removed = RemoveCore(edge.Child, term[edge.Label.Length..]);
        if (!removed)
            return false;

        CompactChildIfNeeded(node, childIndex);
        RecalculateMetadata(node);
        return true;
    }

    private static void CompactChildIfNeeded(Node node, int childIndex)
    {
        if (node.Children is null)
            return;

        var edge = node.Children[childIndex];
        var child = edge.Child;
        if (child.TermFrequency is not 0)
            return;

        if (child.Children is null || child.Children.Count is 0)
        {
            node.Children.RemoveAt(childIndex);
            return;
        }

        if (child.Children.Count is not 1)
            return;

        var grandChildEdge = child.Children[0];
        node.Children[childIndex] = new Edge(edge.Label + grandChildEdge.Label, grandChildEdge.Child);
    }

    private bool TryGetExactNode(ReadOnlySpan<char> term, [NotNullWhen(true)] out Node? node)
    {
        node = _root;
        var remaining = term;

        while (remaining.Length > 0)
        {
            if (node.Children is null)
            {
                node = null;
                return false;
            }

            var childIndex = FindChildIndex(node, remaining[0]);
            if (childIndex is -1)
            {
                node = null;
                return false;
            }

            var edge = node.Children[childIndex];
            if (!remaining.StartsWith(edge.Label, StringComparison.Ordinal))
            {
                node = null;
                return false;
            }

            remaining = remaining[edge.Label.Length..];
            node = edge.Child;
        }

        return true;
    }

    private bool TryGetPrefixNode(ReadOnlySpan<char> prefix, [NotNullWhen(true)] out Node? node, out long prefixFrequency)
    {
        node = _root;
        prefixFrequency = 0;
        var remaining = prefix;
        if (remaining.Length is 0)
        {
            prefixFrequency = node.TermFrequency;
            return true;
        }

        while (remaining.Length > 0)
        {
            if (node.Children is null)
            {
                node = null;
                return false;
            }

            var childIndex = FindChildIndex(node, remaining[0]);
            if (childIndex is -1)
            {
                node = null;
                return false;
            }

            var edge = node.Children[childIndex];
            var commonPrefixLength = StringSearchUtilities.GetCommonPrefixLength(remaining, edge.Label);
            if (commonPrefixLength is 0)
            {
                node = null;
                return false;
            }

            if (commonPrefixLength == remaining.Length)
            {
                node = edge.Child;
                if (commonPrefixLength == edge.Label.Length)
                {
                    prefixFrequency = node.TermFrequency;
                }

                return true;
            }

            if (commonPrefixLength != edge.Label.Length)
            {
                node = null;
                return false;
            }

            remaining = remaining[commonPrefixLength..];
            node = edge.Child;
        }

        prefixFrequency = node.TermFrequency;
        return true;
    }

    private static void CollectTopTerms(Node node, int topK, bool pruning, List<KeyValuePair<string, long>> results)
    {
        if (pruning && results.Count == topK && node.MaxSubtreeFrequency < results[^1].Value)
            return;

        if (node.TermFrequency > 0 && node.Term is not null)
        {
            InsertTopK(results, topK, new KeyValuePair<string, long>(node.Term, node.TermFrequency));
        }

        if (node.Children is null)
            return;

        foreach (var edge in node.Children)
        {
            if (pruning && results.Count == topK && edge.Child.MaxSubtreeFrequency < results[^1].Value)
                break;

            CollectTopTerms(edge.Child, topK, pruning, results);
        }
    }

    private static void InsertTopK(List<KeyValuePair<string, long>> results, int topK, KeyValuePair<string, long> candidate)
    {
        if (topK is 0)
            return;

        var insertIndex = 0;
        while (insertIndex < results.Count)
        {
            var existing = results[insertIndex];
            if (candidate.Value > existing.Value)
                break;

            if (candidate.Value == existing.Value && string.CompareOrdinal(candidate.Key, existing.Key) < 0)
                break;

            insertIndex++;
        }

        if (results.Count < topK || insertIndex < topK)
        {
            results.Insert(insertIndex, candidate);
            if (results.Count > topK)
            {
                results.RemoveAt(topK);
            }
        }
    }

    private static IEnumerable<KeyValuePair<string, long>> EnumerateFrom(Node root)
    {
        if (root.TermFrequency > 0 && root.Term is not null)
            yield return new KeyValuePair<string, long>(root.Term, root.TermFrequency);

        if (root.Children is null)
            yield break;

        var stack = new Stack<Node>();
        for (var i = root.Children.Count - 1; i >= 0; i--)
        {
            stack.Push(root.Children[i].Child);
        }

        while (stack.TryPop(out var node))
        {
            if (node.TermFrequency > 0 && node.Term is not null)
                yield return new KeyValuePair<string, long>(node.Term, node.TermFrequency);

            if (node.Children is null)
                continue;

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i].Child);
            }
        }
    }

    private static void RecalculateMetadata(Node node)
    {
        var maxSubtreeFrequency = node.TermFrequency;
        if (node.Children is not null)
        {
            foreach (var edge in node.Children)
            {
                if (edge.Child.MaxSubtreeFrequency > maxSubtreeFrequency)
                {
                    maxSubtreeFrequency = edge.Child.MaxSubtreeFrequency;
                }
            }

            node.Children.Sort((left, right) =>
            {
                var compareByFrequency = right.Child.MaxSubtreeFrequency.CompareTo(left.Child.MaxSubtreeFrequency);
                if (compareByFrequency is not 0)
                    return compareByFrequency;

                return string.CompareOrdinal(left.Label, right.Label);
            });
        }

        node.MaxSubtreeFrequency = maxSubtreeFrequency;
    }

    private static int FindChildIndex(Node node, char firstChar)
    {
        if (node.Children is null)
            return -1;

        for (var i = 0; i < node.Children.Count; i++)
        {
            if (node.Children[i].Label[0] == firstChar)
                return i;
        }

        return -1;
    }

    private sealed class Edge(string label, Node child)
    {
        public string Label { get; } = label;
        public Node Child { get; } = child;
    }

    private readonly record struct TermFrequency(string Term, long Frequency);

    private sealed class Node
    {
        public string? Term { get; set; }
        public long TermFrequency { get; set; }
        public long MaxSubtreeFrequency { get; set; }
        public List<Edge>? Children { get; set; }
    }
}
