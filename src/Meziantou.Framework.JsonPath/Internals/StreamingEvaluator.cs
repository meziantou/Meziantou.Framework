using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// Evaluates a query on the tokens of a <see cref="Utf8JsonReader"/>, reading the document once from start to end.
/// </summary>
/// <remarks>
/// <para>
/// The query runs as an automaton over the segments: a value holds a count for each segment index it is an input of,
/// and the count of the last index is the number of times the query selects the value. The counts of a child follow from
/// the counts of its parent and its name or index, so most values are decided from their position alone and are never
/// materialized. A value is parsed into a <see cref="JsonNode"/> only when it is selected, when a filter must test it,
/// or when it is an array whose selectors need its length (negative indexes, some slices). Inside a parsed value the
/// same automaton walks the nodes.
/// </para>
/// <para>
/// Matches come in document order, each one as many times as <see cref="JsonPathEvaluator"/> would select it. A query
/// whose filters use <c>$</c> needs the whole document, which is then parsed before the automaton walks it.
/// </para>
/// <para>
/// Feeding tokens is resumable: <see cref="Process"/> stops before a token whose value it cannot materialize yet, and
/// resumes from there when called again with more data.
/// </para>
/// </remarks>
internal sealed class StreamingEvaluator
{
    private readonly Segment[] _segments;
    private readonly byte[]?[][] _utf8Names;
    private readonly bool[] _hasFilter;
    private readonly bool[] _needsLength;
    private readonly bool _needsRoot;
    private readonly EvaluationNavigator<JsonNode> _navigator = new(JsonNodeNavigator.Instance, options: null);
    private readonly List<long[]> _counts = [];
    private readonly List<Frame> _frames = [];
    private JsonNode? _root;
    private bool _hasPendingName;
    private string? _pendingName;

    public StreamingEvaluator(JsonPathExpression expression)
    {
        _segments = expression.Segments;
        _utf8Names = new byte[]?[_segments.Length][];
        _hasFilter = new bool[_segments.Length];
        _needsLength = new bool[_segments.Length];
        for (var i = 0; i < _segments.Length; i++)
        {
            var selectors = _segments[i].Selectors;
            _utf8Names[i] = new byte[]?[selectors.Length];
            for (var j = 0; j < selectors.Length; j++)
            {
                switch (selectors[j])
                {
                    case NameSelector name:
                        _utf8Names[i][j] = Encoding.UTF8.GetBytes(name.Name);
                        break;
                    case FilterSelector filter:
                        _hasFilter[i] = true;
                        _needsRoot |= JsonPathEvaluator.ContainsAbsoluteQuery(filter.Expression);
                        break;
                    case IndexSelector { Index: < 0 }:
                    case SliceSelector slice when !IsForwardOnly(slice):
                        _needsLength[i] = true;
                        break;
                }
            }
        }
    }

    /// <summary>Gets the matches found since the last time the list was cleared, in document order.</summary>
    public List<JsonPathMatch> Matches { get; } = [];

    /// <summary>Gets whether the root value has been read entirely.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Reads tokens until the root value ends or the reader runs out of data.</summary>
    /// <returns>
    /// <see langword="true"/> when the root value has been read; <see langword="false"/> when more data is needed. The
    /// reader then stands before the first token that was not processed.
    /// </returns>
    public bool Process(ref Utf8JsonReader reader)
    {
        while (!IsCompleted)
        {
            var checkpoint = reader;
            if (!reader.Read() || !HandleToken(ref reader))
            {
                reader = checkpoint;
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads a value whose tokens are all available, from the current token when there is one.</summary>
    /// <exception cref="JsonException">The reader does not hold a complete value.</exception>
    /// <exception cref="ArgumentException">The reader returns comments, which a <see cref="JsonNode"/> cannot be parsed from.</exception>
    public void ProcessValue(ref Utf8JsonReader reader)
    {
        if (reader.CurrentState.Options.CommentHandling is JsonCommentHandling.Allow)
            throw new ArgumentException("The reader must skip or disallow comments: values cannot be parsed from a reader that returns them. Use JsonCommentHandling.Skip.", nameof(reader));

        switch (reader.TokenType)
        {
            case JsonTokenType.None or JsonTokenType.PropertyName or JsonTokenType.Comment:
                break;
            case JsonTokenType.EndObject or JsonTokenType.EndArray:
                throw new JsonException("The reader is not positioned on a JSON value.");
            default:
                if (!HandleToken(ref reader))
                    throw IncompleteValue();

                break;
        }

        while (!IsCompleted)
        {
            if (!reader.Read() || !HandleToken(ref reader))
                throw IncompleteValue();
        }

        static JsonException IncompleteValue() => new("The reader does not hold a complete JSON value.");
    }

    private bool HandleToken(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
                HandlePropertyName(ref reader);
                return true;

            case JsonTokenType.EndObject:
            case JsonTokenType.EndArray:
                _frames.RemoveAt(_frames.Count - 1);
                IsCompleted = _frames.Count is 0;
                return true;

            case JsonTokenType.Comment:
                return true;

            default:
                return HandleValue(ref reader);
        }
    }

    // The counts of a member follow from its name, which is only available on this token
    private void HandlePropertyName(ref Utf8JsonReader reader)
    {
        var frame = _frames[^1];
        _hasPendingName = false;
        _pendingName = null;
        if (!frame.IsLive)
            return;

        var key = reader.ValueIsEscaped ? new Key(reader.GetString()!) : new Key(reader.ValueSpan);
        var counts = GetCounts(frame.Depth + 1);
        _hasPendingName = ComputeChildCounts(GetCounts(frame.Depth), counts, key, length: -1) || frame.FiltersChildren;
        if (_hasPendingName)
        {
            _pendingName = key.Name ?? reader.GetString();
        }
    }

    private bool HandleValue(ref Utf8JsonReader reader)
    {
        var isContainer = reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray;
        var isArray = reader.TokenType is JsonTokenType.StartArray;
        PathNode? path = null;
        Span<long> counts;
        bool isLive;
        var filtersValue = false;
        var depth = 0;
        if (_frames.Count is 0)
        {
            counts = GetCounts(0);
            counts.Clear();
            counts[0] = 1;
            isLive = true;
            if (_needsRoot)
            {
                // The filters use $, so the whole document is needed before anything can be selected
                if (!TryParse(ref reader, isContainer, out var root))
                    return false;

                _root = root;
                Walk(root, path: null, depth: 0);
                IsCompleted = true;
                return true;
            }
        }
        else
        {
            var parent = _frames[^1];
            depth = parent.Depth + 1;
            counts = GetCounts(depth);
            if (parent.IsArray)
            {
                isLive = parent.IsLive && (ComputeChildCounts(GetCounts(parent.Depth), counts, new Key(parent.NextIndex), length: -1) || parent.FiltersChildren);
            }
            else
            {
                isLive = _hasPendingName;
            }

            filtersValue = parent.FiltersChildren;
            if (isLive)
            {
                path = parent.IsArray ? PathNode.FromIndex(parent.Path, parent.NextIndex) : PathNode.FromName(parent.Path, _pendingName!);
            }
        }

        if (isLive && (filtersValue || counts[^1] > 0 || (isArray && NeedsLength(counts))))
        {
            if (!TryParse(ref reader, isContainer, out var node))
                return false;

            if (filtersValue)
            {
                var parent = _frames[^1];
                AddFilterCounts(GetCounts(parent.Depth), GetCounts(depth), node);
            }

            Walk(node, path, depth);
            Advance();
            return true;
        }

        if (isContainer)
        {
            if (_frames.Count > 0)
            {
                _frames[^1] = _frames[^1].WithNextIndex();
            }

            isLive &= HasLiveCounts(counts);
            _frames.Add(new Frame(depth, isArray, isLive, isLive ? path : null, isLive && HasFilter(counts), NextIndex: 0));
            return true;
        }

        Advance();
        return true;
    }

    // A value that does not open a container is fully read: count it in its array, and end the evaluation if it is the root
    private void Advance()
    {
        if (_frames.Count is 0)
        {
            IsCompleted = true;
            return;
        }

        _frames[^1] = _frames[^1].WithNextIndex();
    }

    private static bool TryParse(ref Utf8JsonReader reader, bool isContainer, out JsonNode? node)
    {
        if (isContainer)
        {
            var probe = reader;
            if (!probe.TrySkip())
            {
                node = null;
                return false;
            }
        }

        node = JsonNode.Parse(ref reader);
        return true;
    }

    /// <summary>Emits the matches of a parsed value and of its descendants, in document order.</summary>
    /// <param name="node">The value.</param>
    /// <param name="path">The location of the value.</param>
    /// <param name="depth">The depth of the value, whose counts are <c>GetCounts(depth)</c>.</param>
    private void Walk(JsonNode? node, PathNode? path, int depth)
    {
        if (depth > JsonPathEvaluator.MaxRecursionDepth)
            throw new JsonPathEvaluationException($"Maximum recursion depth of {JsonPathEvaluator.MaxRecursionDepth} exceeded at {NormalizedPathBuilder.Build(path)}. The value is too deeply nested.");

        var counts = GetCounts(depth);
        var matchCount = counts[^1];
        if (matchCount > 0)
        {
            if (matchCount > Array.MaxLength - Matches.Count)
                throw new JsonPathEvaluationException("The query selects too many values.");

            var normalizedPath = new NormalizedPath(path);
            for (var i = 0; i < matchCount; i++)
            {
                Matches.Add(new JsonPathMatch(node, normalizedPath));
            }
        }

        if (!HasLiveCounts(counts))
            return;

        var childCounts = GetCounts(depth + 1);
        switch (_navigator.GetKind(node))
        {
            case JsonPathNodeKind.Array:
                var length = _navigator.GetArrayLength(node);
                for (var i = 0; i < length; i++)
                {
                    _navigator.TryGetElement(node, i, out var element);
                    var hasCounts = ComputeChildCounts(GetCounts(depth), childCounts, new Key(i), length);
                    hasCounts |= AddFilterCounts(GetCounts(depth), childCounts, element);
                    if (hasCounts)
                    {
                        Walk(element, PathNode.FromIndex(path, i), depth + 1);
                    }
                }

                break;

            case JsonPathNodeKind.Object:
                foreach (var property in _navigator.GetProperties(node))
                {
                    var hasCounts = ComputeChildCounts(GetCounts(depth), childCounts, new Key(property.Name), length: -1);
                    hasCounts |= AddFilterCounts(GetCounts(depth), childCounts, property.Value);
                    if (hasCounts)
                    {
                        Walk(property.Value, PathNode.FromName(path, property.Name), depth + 1);
                    }
                }

                break;
        }
    }

    /// <summary>Computes the counts of a child from the counts of its parent and its name or index, ignoring filters.</summary>
    /// <param name="parent">The counts of the parent.</param>
    /// <param name="child">Receives the counts of the child.</param>
    /// <param name="key">The name or the index of the child.</param>
    /// <param name="length">The length of the parent array, or -1 when it is not known yet.</param>
    /// <returns>Whether a count of the child is not zero.</returns>
    private bool ComputeChildCounts(ReadOnlySpan<long> parent, Span<long> child, Key key, long length)
    {
        child.Clear();
        var hasCounts = false;
        for (var i = 0; i < _segments.Length; i++)
        {
            var count = parent[i];
            if (count is 0)
                continue;

            var segment = _segments[i];
            if (segment.Kind is SegmentKind.Descendant)
            {
                // Every descendant of an input of a descendant segment is an input of the segment too
                child[i] = Add(child[i], count);
                hasCounts = true;
            }

            var selectors = segment.Selectors;
            for (var j = 0; j < selectors.Length; j++)
            {
                var selected = selectors[j] switch
                {
                    NameSelector name => !key.IsIndex && key.NameEquals(name.Name, _utf8Names[i][j]!),
                    WildcardSelector => true,
                    IndexSelector index => key.IsIndex && IndexContains(index.Index, key.Index, length),
                    SliceSelector slice => key.IsIndex && SliceContains(slice, key.Index, length),
                    _ => false,
                };

                if (selected)
                {
                    child[i + 1] = Add(child[i + 1], count);
                    hasCounts = true;
                }
            }
        }

        return hasCounts;
    }

    /// <summary>Adds the counts that the filter selectors of the parent give to a child.</summary>
    private bool AddFilterCounts(ReadOnlySpan<long> parent, Span<long> child, JsonNode? value)
    {
        var hasCounts = false;
        for (var i = 0; i < _segments.Length; i++)
        {
            if (parent[i] is 0 || !_hasFilter[i])
                continue;

            foreach (var selector in _segments[i].Selectors)
            {
                if (selector is FilterSelector filter && JsonPathEvaluator.EvaluateFilter(filter.Expression, value, _root, _navigator))
                {
                    child[i + 1] = Add(child[i + 1], parent[i]);
                    hasCounts = true;
                }
            }
        }

        return hasCounts;
    }

    // The selectors of a live segment need the length of the array to select its elements
    private bool NeedsLength(ReadOnlySpan<long> counts)
    {
        for (var i = 0; i < _segments.Length; i++)
        {
            if (counts[i] is not 0 && _needsLength[i])
                return true;
        }

        return false;
    }

    // Whether the children of the value can be selected: a count of a segment other than the last one is not zero
    private static bool HasLiveCounts(ReadOnlySpan<long> counts)
    {
        return counts[..^1].ContainsAnyExcept(0);
    }

    private bool HasFilter(ReadOnlySpan<long> counts)
    {
        for (var i = 0; i < _segments.Length; i++)
        {
            if (counts[i] is not 0 && _hasFilter[i])
                return true;
        }

        return false;
    }

    private Span<long> GetCounts(int depth)
    {
        while (_counts.Count <= depth)
        {
            _counts.Add(new long[_segments.Length + 1]);
        }

        return _counts[depth];
    }

    // Both counts are positive
    private static long Add(long left, long right)
    {
        if (right > long.MaxValue - left)
            throw new JsonPathEvaluationException("The query selects too many values.");

        return left + right;
    }

    private static bool IsForwardOnly(SliceSelector slice)
    {
        return slice.Step is null or >= 0 && slice.Start is null or >= 0 && slice.End is null or >= 0;
    }

    private static bool IndexContains(long index, long element, long length)
    {
        if (index < 0)
        {
            index += length;
        }

        return index == element;
    }

    /// <summary>Gets whether a slice selects an element, as RFC 9535 section 2.3.4.2.2 defines it.</summary>
    /// <param name="slice">The slice.</param>
    /// <param name="element">The index of the element.</param>
    /// <param name="length">The length of the array, or -1 when the slice is forward-only and the length is not known.</param>
    private static bool SliceContains(SliceSelector slice, long element, long length)
    {
        var step = slice.Step ?? 1;
        if (step is 0)
            return false;

        if (length < 0)
        {
            // A forward-only slice: the bounds are not negative, so they do not depend on the length
            var start = slice.Start ?? 0;
            return element >= start && (slice.End is not { } end || element < end) && (element - start) % step is 0;
        }

        if (step > 0)
        {
            var lower = Math.Min(Math.Max(Normalize(slice.Start ?? 0, length), 0), length);
            var upper = Math.Min(Math.Max(Normalize(slice.End ?? length, length), 0), length);
            return element >= lower && element < upper && (element - lower) % step is 0;
        }
        else
        {
            var upper = Math.Min(Math.Max(slice.Start is { } start ? Normalize(start, length) : length - 1, -1), length - 1);
            var lower = Math.Min(Math.Max(slice.End is { } end ? Normalize(end, length) : -1, -1), length - 1);
            return element > lower && element <= upper && (upper - element) % step is 0;
        }

        static long Normalize(long index, long length) => index >= 0 ? index : length + index;
    }

    /// <summary>A container being read, whose members are decided as their tokens come.</summary>
    private readonly record struct Frame(int Depth, bool IsArray, bool IsLive, PathNode? Path, bool FiltersChildren, int NextIndex)
    {
        public Frame WithNextIndex() => this with { NextIndex = NextIndex + 1 };
    }

    /// <summary>The name or the index of a child.</summary>
    private readonly ref struct Key
    {
        private readonly ReadOnlySpan<byte> _utf8Name;

        public Key(int index)
        {
            IsIndex = true;
            Index = index;
        }

        public Key(string name)
        {
            Name = name;
        }

        // An unescaped name, as it is in the document
        public Key(ReadOnlySpan<byte> utf8Name)
        {
            _utf8Name = utf8Name;
        }

        public bool IsIndex { get; }

        public int Index { get; }

        public string? Name { get; }

        public bool NameEquals(string name, byte[] utf8Name)
        {
            return Name is not null ? string.Equals(Name, name, StringComparison.Ordinal) : _utf8Name.SequenceEqual(utf8Name);
        }
    }
}
