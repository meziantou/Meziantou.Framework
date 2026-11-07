using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The type of a <see cref="Highlight"/> (https://drafts.csswg.org/css-highlight-api-1/#enumdef-highlighttype).</summary>
/// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#enumdef-highlighttype"/>
[IdlInterface("HighlightType", "https://drafts.csswg.org/css-highlight-api-1/#enumdef-highlighttype")]
public enum HighlightType
{
    /// <summary>A custom highlight.</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlighttype-highlight"/>
    [IdlMember("highlight", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlighttype-highlight")]
    Highlight,

    /// <summary>A highlight for a spelling error.</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlighttype-spelling-error"/>
    [IdlMember("spelling-error", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlighttype-spelling-error")]
    SpellingError,

    /// <summary>A highlight for a grammar error.</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlighttype-grammar-error"/>
    [IdlMember("grammar-error", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlighttype-grammar-error")]
    GrammarError,
}

/// <summary>
/// A custom highlight (https://drafts.csswg.org/css-highlight-api-1/#highlight): a set of ranges, styled by the
/// <c>::highlight(name)</c> pseudo-element once the highlight is registered in a <see cref="HighlightRegistry"/> under that name.
/// It is a <c>setlike&lt;AbstractRange&gt;</c>: the ranges are enumerated in insertion order, and, as for a JavaScript
/// <c>Set</c>, an enumeration sees the changes made while it is in progress.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#highlight"/>
[IdlInterface("Highlight", "https://drafts.csswg.org/css-highlight-api-1/#highlight")]
public sealed class Highlight : IReadOnlyCollection<AbstractRange>
{
    // The ranges in insertion order; a deleted range is null until the list is compacted (https://tc39.es/ecma262/#sec-set.prototype.delete)
    private List<AbstractRange?> _entries = [];
    private readonly Dictionary<AbstractRange, int> _indexes = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates a highlight that contains the ranges (https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-highlight).</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-highlight"/>
    [IdlMember("constructor", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-highlight")]
    public Highlight(params ReadOnlySpan<AbstractRange> initialRanges)
    {
        foreach (var range in initialRanges)
        {
            Add(range);
        }
    }

    /// <summary>
    /// The priority of the highlight (https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-priority): the highlights
    /// with a higher priority are painted over the other ones.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-priority"/>
    [IdlMember("priority", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-priority")]
    public int Priority { get; set; }

    /// <summary>The semantic of the highlight, for the assistive technologies (https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-type).</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-type"/>
    [IdlMember("type", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlight-type")]
    public HighlightType Type { get; set; } = HighlightType.Highlight;

    /// <summary>Gets the number of ranges.</summary>
    [NotInIdl("The size member of setlike<AbstractRange>")]
    public int Count => _indexes.Count;

    /// <summary>Returns whether the highlight contains the range.</summary>
    [NotInIdl("The has() member of setlike<AbstractRange>")]
    public bool Has(AbstractRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return _indexes.ContainsKey(range);
    }

    /// <summary>Adds a range.</summary>
    [NotInIdl("The add() member of setlike<AbstractRange>")]
    public void Add(AbstractRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (_indexes.TryAdd(range, _entries.Count))
        {
            _entries.Add(range);
        }
    }

    /// <summary>Removes a range. Returns whether the highlight contained the range.</summary>
    [NotInIdl("The delete() member of setlike<AbstractRange>")]
    public bool Delete(AbstractRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!_indexes.Remove(range, out var index))
            return false;

        _entries[index] = null;
        CompactIfNeeded();
        return true;
    }

    /// <summary>Removes all the ranges.</summary>
    [NotInIdl("The clear() member of setlike<AbstractRange>")]
    public void Clear()
    {
        _indexes.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            _entries[i] = null;
        }

        CompactIfNeeded();
    }

    /// <summary>
    /// Replaces the list when most entries are deleted, so that it does not grow forever. The enumerations in progress keep
    /// the previous list, where the ranges added later are not visible.
    /// </summary>
    private void CompactIfNeeded()
    {
        if (_entries.Count < 32 || _indexes.Count * 2 > _entries.Count)
            return;

        var entries = new List<AbstractRange?>(_indexes.Count);
        foreach (var entry in _entries)
        {
            if (entry is not null)
            {
                _indexes[entry] = entries.Count;
                entries.Add(entry);
            }
        }

        _entries = entries;
    }

    /// <summary>Enumerates the ranges, including the ranges added during the enumeration.</summary>
    [NotInIdl("The iterator of setlike<AbstractRange>")]
    public IEnumerator<AbstractRange> GetEnumerator()
    {
        var entries = _entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is { } entry)
                yield return entry;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The custom highlights registered for a document (https://drafts.csswg.org/css-highlight-api-1/#highlight-registry),
/// returned by <c>CSS.highlights</c> and <see cref="Document.Highlights"/>. It is a <c>maplike&lt;DOMString, Highlight&gt;</c>:
/// the entries are enumerated in insertion order (replacing the highlight of a name keeps its position), and, as for a
/// JavaScript <c>Map</c>, an enumeration sees the changes made while it is in progress.
/// </summary>
/// <remarks>
/// The registered highlights do not change the computed styles (the <c>::highlight(name)</c> pseudo-elements are styled
/// whether a highlight is registered under the name or not). <c>highlightsFromPoint()</c> hit tests the client rectangles of
/// the ranges (see <see cref="HighlightsFromPoint"/>).
/// </remarks>
/// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#highlightregistry"/>
[IdlInterface("HighlightRegistry", "https://drafts.csswg.org/css-highlight-api-1/#highlightregistry")]
public sealed class HighlightRegistry : IReadOnlyCollection<KeyValuePair<string, Highlight>>
{
    // The entries in insertion order; a deleted entry is null until the list is compacted (https://tc39.es/ecma262/#sec-map.prototype.delete)
    private List<Entry?> _entries = [];
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);
    private readonly Document _document;

    internal HighlightRegistry(Document document)
    {
        _document = document;
    }

    /// <summary>Gets the number of registered highlights.</summary>
    [NotInIdl("The size member of maplike<DOMString, Highlight>")]
    public int Count => _indexes.Count;

    /// <summary>Returns the highlight registered under the name, or null.</summary>
    [NotInIdl("The get() member of maplike<DOMString, Highlight>")]
    public Highlight? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _indexes.TryGetValue(name, out var index) ? _entries[index]!.Highlight : null;
    }

    /// <summary>Returns whether a highlight is registered under the name.</summary>
    [NotInIdl("The has() member of maplike<DOMString, Highlight>")]
    public bool Has(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _indexes.ContainsKey(name);
    }

    /// <summary>Registers the highlight under the name, replacing the highlight already registered under it (which keeps its position).</summary>
    [NotInIdl("The set() member of maplike<DOMString, Highlight>")]
    public HighlightRegistry Set(string name, Highlight highlight)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(highlight);
        if (_indexes.TryGetValue(name, out var index))
        {
            _entries[index]!.Highlight = highlight;
        }
        else
        {
            _indexes.Add(name, _entries.Count);
            _entries.Add(new Entry(name, highlight));
        }

        return this;
    }

    /// <summary>Unregisters the highlight of the name. Returns whether a highlight was registered under the name.</summary>
    [NotInIdl("The delete() member of maplike<DOMString, Highlight>")]
    public bool Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_indexes.Remove(name, out var index))
            return false;

        _entries[index] = null;
        CompactIfNeeded();
        return true;
    }

    /// <summary>Unregisters all the highlights.</summary>
    [NotInIdl("The clear() member of maplike<DOMString, Highlight>")]
    public void Clear()
    {
        _indexes.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            _entries[i] = null;
        }

        CompactIfNeeded();
    }

    /// <summary>
    /// Replaces the list when most entries are deleted, so that it does not grow forever. The enumerations in progress keep
    /// the previous list, where the entries added later are not visible.
    /// </summary>
    private void CompactIfNeeded()
    {
        if (_entries.Count < 32 || _indexes.Count * 2 > _entries.Count)
            return;

        var entries = new List<Entry?>(_indexes.Count);
        foreach (var entry in _entries)
        {
            if (entry is not null)
            {
                _indexes[entry.Name] = entries.Count;
                entries.Add(entry);
            }
        }

        _entries = entries;
    }

    /// <summary>Enumerates the names and their highlights, including the entries added during the enumeration.</summary>
    [NotInIdl("The iterator of maplike<DOMString, Highlight>")]
    public IEnumerator<KeyValuePair<string, Highlight>> GetEnumerator()
    {
        var entries = _entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is { } entry)
                yield return new KeyValuePair<string, Highlight>(entry.Name, entry.Highlight);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the highlights hit at a point of the viewport (https://drafts.csswg.org/css-highlight-api-1/#interactions), with
    /// their ranges whose client rectangles (<see cref="Dom.Range.GetClientRects"/>) contain the point, by descending priority (the
    /// highlight registered last first when the priorities are equal). Nothing is returned for a point outside of the viewport,
    /// or when the topmost box at the point is in a shadow tree that is not in <paramref name="options"/>; the ranges of another
    /// tree than the node at the point (e.g. a range of the light tree around a shadow host) do not highlight it.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlightregistry-highlightsfrompoint"/>
    [IdlMember("highlightsFromPoint", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlightregistry-highlightsfrompoint")]
    public IReadOnlyList<HighlightHitResult> HighlightsFromPoint(float x, float y, HighlightsFromPointOptions? options = null)
    {
        // "If any of the following are true, return the empty sequence": x or y is negative or greater than the size of the
        // viewport excluding the scroll bars
        if (_indexes.Count == 0 || x < 0 || y < 0 || !CssomViewGeometry.IsInViewport(_document, x, y))
            return [];

        var engine = _document.GetLayoutEngine();
        if (engine.EnsureLayout() is null)
            return [];

        // "The topmost box in the viewport in paint order that would be a target for hit testing at coordinates x,y [...] has an
        // element associated to it that's in a shadow tree whose shadow root is not contained by options.shadowRoots"
        var hit = HitTesting.HitTest(engine, new PhysicalOffset(x, y));
        if (hit is { } topmost && topmost.Element.GetRoot() is ShadowRoot shadowRoot && !Contains(options?.ShadowRoots, shadowRoot))
            return [];

        // The node painted at the point: a range highlights the text of its own tree only (a range of the light tree that
        // contains a shadow host does not highlight the text of its shadow tree)
        Node? hitNode = hit is { Fragment: TextFragment { Run.Node: { } hitText } } ? hitText : hit?.Element;

        var results = new List<(HighlightHitResult Result, int Priority, int Order)>();
        var order = 0;
        foreach (var (_, highlight) in this)
        {
            order++;
            List<AbstractRange>? ranges = null;
            foreach (var abstractRange in highlight)
            {
                // "If abstractRange is an invalid StaticRange, then continue"
                if (abstractRange is StaticRange staticRange && !staticRange.IsValid)
                    continue;

                if (abstractRange.Collapsed || (hitNode is not null && !ReferenceEquals(hitNode.GetRoot(), abstractRange.StartContainer.GetRoot())))
                    continue;

                var range = new Dom.Range(abstractRange.StartContainer, abstractRange.StartOffset, abstractRange.EndContainer, abstractRange.EndOffset);
                foreach (var quad in range.GetClientQuads())
                {
                    if (quad.Bounds.Contains(x, y))
                    {
                        (ranges ??= []).Add(abstractRange);
                        break;
                    }
                }
            }

            if (ranges is not null)
            {
                results.Add((new HighlightHitResult { Highlight = highlight, Ranges = ranges }, highlight.Priority, order));
            }
        }

        // "Sort results by descending order of priority": the highlight registered last has the higher effective priority
        results.Sort(static (a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : b.Order.CompareTo(a.Order));
        return results.ConvertAll(result => result.Result);

        static bool Contains(IReadOnlyList<ShadowRoot>? shadowRoots, ShadowRoot shadowRoot)
        {
            if (shadowRoots is null)
                return false;

            foreach (var candidate in shadowRoots)
            {
                if (ReferenceEquals(candidate, shadowRoot))
                    return true;
            }

            return false;
        }
    }

    private sealed class Entry(string name, Highlight highlight)
    {
        public string Name { get; } = name;

        public Highlight Highlight { get; set; } = highlight;
    }
}

/// <summary>A highlight hit at a point and its ranges that contain the point (https://drafts.csswg.org/css-highlight-api-1/#dictdef-highlighthitresult).</summary>
/// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dictdef-highlighthitresult"/>
[IdlInterface("HighlightHitResult", "https://drafts.csswg.org/css-highlight-api-1/#dictdef-highlighthitresult")]
public sealed class HighlightHitResult
{
    /// <summary>Gets the highlight.</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlighthitresult-highlight"/>
    [IdlMember("highlight", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlighthitresult-highlight")]
    public Highlight? Highlight { get; init; }

    /// <summary>Gets the ranges of the highlight that contain the point.</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlighthitresult-ranges"/>
    [IdlMember("ranges", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlighthitresult-ranges")]
    public IReadOnlyList<AbstractRange> Ranges { get; init; } = [];
}

/// <summary>The options of <see cref="HighlightRegistry.HighlightsFromPoint"/> (https://drafts.csswg.org/css-highlight-api-1/#dictdef-highlightsfrompointoptions).</summary>
/// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dictdef-highlightsfrompointoptions"/>
[IdlInterface("HighlightsFromPointOptions", "https://drafts.csswg.org/css-highlight-api-1/#dictdef-highlightsfrompointoptions")]
public sealed class HighlightsFromPointOptions
{
    /// <summary>Gets the shadow roots whose highlights may be returned.</summary>
    /// <seealso href="https://drafts.csswg.org/css-highlight-api-1/#dom-highlightsfrompointoptions-shadowroots"/>
    [IdlMember("shadowRoots", "https://drafts.csswg.org/css-highlight-api-1/#dom-highlightsfrompointoptions-shadowroots")]
    public IReadOnlyList<ShadowRoot> ShadowRoots { get; init; } = [];
}
