using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>
/// The features of a complex selector that let the matching reject most elements without matching the selector: the key of
/// its subject (the local name, an ID and a class its subject must have), and the hashes of the local names, IDs and classes
/// its ancestors must have (see <see cref="SelectorAncestorFilter"/>).
/// </summary>
internal sealed class SelectorHints
{
    /// <summary>The maximum number of ancestor hashes of a selector (as in Blink and WebKit).</summary>
    public const int MaxAncestorHashes = 4;

    private SelectorHints()
    {
    }

    /// <summary>Whether the selector never matches an element (it has a pseudo-element).</summary>
    public bool NeverMatches { get; private init; }

    /// <summary>The local name of a type selector of the subject, or null.</summary>
    public string? SubjectLocalName { get; private init; }

    /// <summary>The ASCII lowercase <see cref="SubjectLocalName"/>, for the HTML elements of an HTML document.</summary>
    public string? SubjectLowercaseLocalName { get; private init; }

    /// <summary>The tag id of <see cref="SubjectLowercaseLocalName"/>, for the HTML elements of an HTML document.</summary>
    public TagId SubjectTagId { get; private init; }

    /// <summary>An ID the subject must have (case-sensitive outside of quirks mode), or null.</summary>
    public string? SubjectId { get; private init; }

    /// <summary>A class the subject must have (case-sensitive outside of quirks mode), or null.</summary>
    public string? SubjectClass { get; private init; }

    /// <summary>
    /// Whether the selector is a single compound selector made only of type, ID, class and attribute selectors: it is matched
    /// without the combinators nor the context (see <see cref="SelectorMatcher.MatchesSimpleCompound"/>).
    /// </summary>
    public bool IsSimpleCompound { get; private init; }

    /// <summary>The hashes of the features the ancestors of the subject must have (see <see cref="SelectorAncestorFilter"/>).</summary>
    public uint[] AncestorHashes { get; private init; } = [];

    public static SelectorHints Create(ComplexSelector selector)
    {
        // (the cascade matches the selectors ending with a pseudo-element against their originating element: the ancestors are the same)
        if (selector.HasPseudoElement)
            return new SelectorHints { NeverMatches = true, AncestorHashes = selector.RelativeCombinator is null ? GetAncestorHashes(selector) : [] };

        var subject = selector.Compounds[^1];
        string? localName = null, id = null, className = null;
        var isSimpleCompound = selector.Compounds.Length == 1 && selector.RelativeCombinator is null;
        foreach (var simple in subject.Selectors)
        {
            switch (simple)
            {
                case TypeSelector type:
                    localName ??= type.LocalName;
                    break;
                case IdSelector idSelector:
                    id ??= idSelector.Id;
                    break;
                case ClassSelector classSelector:
                    className ??= classSelector.ClassName;
                    break;
                case AttributeSelector:
                    break;
                default:
                    isSimpleCompound = false;
                    break;
            }
        }

        var lowercaseLocalName = localName is null ? null : Infra.InfraAscii.ToAsciiLowercase(localName);
        return new SelectorHints
        {
            SubjectLocalName = localName,
            SubjectLowercaseLocalName = lowercaseLocalName,
            SubjectTagId = lowercaseLocalName is null ? TagId.Unknown : TagNames.GetTagId(lowercaseLocalName),
            SubjectId = id,
            SubjectClass = className,
            IsSimpleCompound = isSimpleCompound,
            AncestorHashes = selector.RelativeCombinator is null ? GetAncestorHashes(selector) : [],
        };
    }

    /// <summary>
    /// Returns whether the subject key of the selector rejects an element of a tree that is not the featureless shadow host:
    /// its local name, its ID or its classes cannot match the subject. The ID and the class are only checked when
    /// <paramref name="checkIdAndClass"/> is true: outside of quirks mode, when the classes are not animated.
    /// </summary>
    public bool Rejects(Element element, bool isHtmlDocument, bool checkIdAndClass)
    {
        if (SubjectLocalName is not null)
        {
            if (isHtmlDocument && element.IsHtmlNamespace)
            {
                if (SubjectTagId != TagId.Unknown ? element.TagId != SubjectTagId : SubjectLowercaseLocalName != element.LocalName)
                    return true;
            }
            else if (SubjectLocalName != element.LocalName)
            {
                return true;
            }
        }

        if (!checkIdAndClass)
            return false;

        if (SubjectId is not null && SubjectId != element.IdValue)
            return true;

        return SubjectClass is not null && !element.GetClassNames().Contains(SubjectClass);
    }

    /// <summary>
    /// Returns the hashes of the local names, IDs and classes of the compound selectors that must match ancestors of the
    /// subject: the compound selectors on the left of a descendant or a child combinator (the compound selectors on the left
    /// of a sibling combinator match siblings of the subject or of its ancestors). The arguments of the pseudo-classes
    /// (:is(), :not()...) are ignored.
    /// </summary>
    private static uint[] GetAncestorHashes(ComplexSelector selector)
    {
        Span<uint> hashes = stackalloc uint[MaxAncestorHashes];
        var count = 0;
        for (var i = selector.Compounds.Length - 2; i >= 0 && count < MaxAncestorHashes; i--)
        {
            if (selector.Combinators[i] is not (Combinator.Descendant or Combinator.Child))
                continue;

            // The most selective features first: the ID, the classes, then the local name
            var compound = selector.Compounds[i];
            foreach (var simple in compound.Selectors)
            {
                if (simple is IdSelector id && count < MaxAncestorHashes)
                {
                    hashes[count++] = SelectorAncestorFilter.HashId(id.Id);
                }
            }

            foreach (var simple in compound.Selectors)
            {
                if (simple is ClassSelector classSelector && count < MaxAncestorHashes)
                {
                    hashes[count++] = SelectorAncestorFilter.HashClass(classSelector.ClassName);
                }
            }

            foreach (var simple in compound.Selectors)
            {
                if (simple is TypeSelector { LocalName: { } localName } && count < MaxAncestorHashes)
                {
                    hashes[count++] = SelectorAncestorFilter.HashLocalName(localName);
                }
            }
        }

        return count == 0 ? [] : hashes[..count].ToArray();
    }
}

/// <summary>
/// A counting Bloom filter of the local names, IDs and classes of the ancestors of the elements being matched (as in Blink and
/// WebKit): a selector whose ancestors must have a feature that no ancestor has cannot match, so its descendant and child
/// combinators are not matched. The filter may contain more features than the ancestors have (false positives), never less:
/// the features are ASCII case-folded, so that the type selectors of the HTML elements and the IDs and classes of the
/// documents in quirks mode (matched ASCII case-insensitively) are in the filter whatever their case.
/// </summary>
internal sealed class SelectorAncestorFilter
{
    private const int KeyBits = 12;
    private const uint KeyMask = (1 << KeyBits) - 1;

    private readonly byte[] _counters = new byte[1 << KeyBits];

    /// <summary>The hashes of the elements of the stack, in the order they were added.</summary>
    private readonly List<uint> _hashes = [];

    /// <summary>The elements of the stack, from the root, and the index of their first hash in <see cref="_hashes"/>.</summary>
    private readonly List<(Element Element, int HashStart)> _elements = [];

    /// <summary>
    /// The number of elements of the stack whose features are in the filter: the features are added when a selector is
    /// checked, so that walking the subtrees whose elements the subject keys reject (most of the tree for a selector such as
    /// "div a") does not hash their ancestors.
    /// </summary>
    private int _hashedCount;

    private readonly List<Element> _chain = [];

    /// <summary>Whether a counter may be non-zero (a saturated counter is not decremented).</summary>
    private bool _dirty;

    /// <summary>The document and its <see cref="Document.DomVersion"/> when the elements were added: the features of the elements change with the DOM.</summary>
    private Document? _document;
    private int _domVersion;

    public static uint HashLocalName(string localName) => Hash(localName, 0x9E3779B9);

    public static uint HashId(string id) => Hash(id, 0x85EBCA6B);

    public static uint HashClass(string className) => Hash(className, 0xC2B2AE35);

    private static uint Hash(ReadOnlySpan<char> value, uint seed)
    {
        // FNV-1a of the ASCII lowercase characters, then the finalizer of MurmurHash3 so that the bits of both keys are mixed
        var hash = 2166136261u ^ seed;
        foreach (var c in value)
        {
            hash = (hash ^ (c is >= 'A' and <= 'Z' ? (uint)(c + 0x20) : c)) * 16777619u;
        }

        hash ^= hash >> 16;
        hash *= 0x85EBCA6B;
        hash ^= hash >> 13;
        hash *= 0xC2B2AE35;
        hash ^= hash >> 16;
        return hash;
    }

    /// <summary>Returns the parent of an element for the filter: the shadow host is the parent of the elements of its shadow tree.</summary>
    public static Element? GetParent(Element element) => element.ParentNode switch
    {
        Element parent => parent,
        ShadowRoot shadowRoot => shadowRoot.Host,
        _ => null,
    };

    /// <summary>Returns whether the ancestors may match the compound selectors on the left of the descendant and child combinators of a selector.</summary>
    public bool MayMatch(SelectorHints hints)
    {
        if (_hashedCount < _elements.Count)
        {
            AddPendingFeatures();
        }

        foreach (var hash in hints.AncestorHashes)
        {
            if (_counters[hash & KeyMask] == 0 || _counters[(hash >> KeyBits) & KeyMask] == 0)
                return false;
        }

        return true;
    }

    /// <summary>Adds an element to the stack: the parent of the next elements to match (or of the next ancestor added).</summary>
    public void Push(Element element) => _elements.Add((element, -1));

    /// <summary>Removes the last element added, and its features.</summary>
    public void Pop()
    {
        var index = _elements.Count - 1;
        if (index < _hashedCount)
        {
            var start = _elements[index].HashStart;
            for (var i = start; i < _hashes.Count; i++)
            {
                var hash = _hashes[i];
                Decrement(hash & KeyMask);
                Decrement((hash >> KeyBits) & KeyMask);
            }

            _hashes.RemoveRange(start, _hashes.Count - start);
            _hashedCount = index;
        }

        _elements.RemoveAt(index);
    }

    /// <summary>Adds the features of the elements of the stack that are not in the filter yet.</summary>
    private void AddPendingFeatures()
    {
        for (var i = _hashedCount; i < _elements.Count; i++)
        {
            var element = _elements[i].Element;
            _elements[i] = (element, _hashes.Count);
            Add(HashLocalName(element.LocalName));
            if (element.IdValue is { Length: > 0 } id)
            {
                Add(HashId(id));
            }

            foreach (var className in element.GetStyleClassNames())
            {
                Add(HashClass(className));
            }
        }

        _hashedCount = _elements.Count;
    }

    /// <summary>Adds the inclusive ancestors of an element (from the root), after the ones already added for an element of another document or DOM version are removed.</summary>
    public void PushInclusiveAncestors(Element? element)
    {
        _chain.Clear();
        for (var ancestor = element; ancestor is not null; ancestor = GetParent(ancestor))
        {
            _chain.Add(ancestor);
        }

        for (var i = _chain.Count - 1; i >= 0; i--)
        {
            Push(_chain[i]);
        }

        _chain.Clear();
    }

    /// <summary>
    /// Makes the filter contain the features of the inclusive ancestors of <paramref name="parent"/>, the parent of the next
    /// elements to match: the elements of the stack that are still ancestors are kept, so that the elements matched in tree
    /// order only add their parent. The filter is cleared when the DOM changed.
    /// </summary>
    public void SetParent(Document document, Element? parent)
    {
        if (!ReferenceEquals(_document, document) || _domVersion != document.DomVersion)
        {
            Clear();
            _document = document;
            _domVersion = document.DomVersion;
        }
        else if (_elements.Count > 0 ? ReferenceEquals(_elements[^1].Element, parent) : parent is null)
        {
            return;
        }

        _chain.Clear();
        for (var ancestor = parent; ancestor is not null; ancestor = GetParent(ancestor))
        {
            _chain.Add(ancestor);
        }

        var common = 0;
        var max = Math.Min(_elements.Count, _chain.Count);
        while (common < max && ReferenceEquals(_elements[common].Element, _chain[_chain.Count - 1 - common]))
        {
            common++;
        }

        while (_elements.Count > common)
        {
            Pop();
        }

        for (var i = _chain.Count - 1 - common; i >= 0; i--)
        {
            Push(_chain[i]);
        }

        _chain.Clear();
    }

    /// <summary>Removes all the elements.</summary>
    public void Clear()
    {
        if (_dirty)
        {
            Array.Clear(_counters);
            _hashes.Clear();
            _dirty = false;
        }

        _elements.Clear();
        _hashedCount = 0;
        _document = null;
    }

    private void Add(uint hash)
    {
        _hashes.Add(hash);
        _dirty = true;
        Increment(hash & KeyMask);
        Increment((hash >> KeyBits) & KeyMask);
    }

    private void Increment(uint index)
    {
        // A saturated counter is never decremented: it only causes false positives
        ref var counter = ref _counters[index];
        if (counter != byte.MaxValue)
        {
            counter++;
        }
    }

    private void Decrement(uint index)
    {
        ref var counter = ref _counters[index];
        if (counter != byte.MaxValue)
        {
            counter--;
        }
    }
}
