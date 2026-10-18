using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>An enumerated attribute (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#enumerated-attribute).</summary>
/// <remarks>
/// Keywords are lowercase and map to the index of their state. CanonicalKeywords gives the canonical keyword of each
/// state (null when the state has no keyword). The defaults are state indexes, or -1 when not defined.
/// </remarks>
internal sealed record EnumeratedAttribute(
    IReadOnlyDictionary<string, int> Keywords,
    string?[] CanonicalKeywords,
    int MissingValueDefault,
    int InvalidValueDefault,
    int EmptyValueDefault)
{
    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#enumerated-attribute (returns -1 for no state).</summary>
    public int GetState(string? value)
    {
        if (value is null)
            return MissingValueDefault;

        if (Keywords.TryGetValue(InfraAscii.ToAsciiLowercase(value), out var state))
            return state;

        if (EmptyValueDefault >= 0 && value.Length == 0)
            return EmptyValueDefault;

        return InvalidValueDefault;
    }
}

/// <summary>
/// The reflection of content attributes in IDL attributes
/// (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#reflecting-content-attributes-in-idl-attributes).
/// </summary>
internal static partial class Reflection
{
    private const long MaxUnsigned = 2147483647;

    // DOMString

    public static string GetString(Element element, string name) => element.GetAttributeValue(name) ?? "";

    public static void SetString(Element element, string name, string value) => element.SetAttributeValue(name, value);

    public static string? GetNullableString(Element element, string name) => element.GetAttributeValue(name);

    public static void SetNullableString(Element element, string name, string? value)
    {
        if (value is null)
        {
            element.RemoveAttributeByLocalName(name);
        }
        else
        {
            element.SetAttributeValue(name, value);
        }
    }

    /// <summary>An enumerated attribute limited to only known values.</summary>
    public static string GetEnumerated(Element element, string name, EnumeratedAttribute definition)
    {
        var state = definition.GetState(element.GetAttributeValue(name));
        return state < 0 ? "" : definition.CanonicalKeywords[state] ?? "";
    }

    /// <summary>A nullable enumerated attribute limited to only known values.</summary>
    public static string? GetNullableEnumerated(Element element, string name, EnumeratedAttribute definition)
    {
        var state = definition.GetState(element.GetAttributeValue(name));
        return state < 0 ? null : definition.CanonicalKeywords[state];
    }

    // USVString

    public static string GetUsvString(Element element, string name) => InfraString.ToScalarValueString(element.GetAttributeValue(name) ?? "");

    /// <summary>A USVString reflected as a URL.</summary>
    public static string GetUrl(Element element, string name)
    {
        var value = element.GetAttributeValue(name);
        if (value is null)
            return "";

        var document = element.NodeDocument;
        var url = UrlParser.Parse(value, document.DocumentBaseUrl, WebEncodingRegistryHelper.GetUrlEncoding(document));
        return url is not null ? url.Serialize() : InfraString.ToScalarValueString(value);
    }

    // boolean

    public static bool GetBoolean(Element element, string name) => element.GetAttributeValue(name) is not null;

    public static void SetBoolean(Element element, string name, bool value)
    {
        if (value)
        {
            element.SetAttributeValue(name, "");
        }
        else
        {
            element.RemoveAttributeByLocalName(name);
        }
    }

    // long

    public static int GetLong(Element element, string name, bool nonNegative, int? defaultValue)
    {
        var value = element.GetAttributeValue(name);
        if (value is not null)
        {
            var success = nonNegative ? Microsyntaxes.TryParseNonNegativeInteger(value, out var parsed) : Microsyntaxes.TryParseInteger(value, out parsed);
            if (success && parsed is >= int.MinValue and <= int.MaxValue)
                return (int)parsed;
        }

        if (defaultValue is { } defaultResult)
            return defaultResult;

        return nonNegative ? -1 : 0;
    }

    public static void SetLong(Element element, string name, bool nonNegative, int value)
    {
        if (nonNegative && value < 0)
            throw DomException.IndexSize("The value must be non-negative");

        element.SetAttributeValue(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // unsigned long

    // positive: limited to only positive numbers (or positive numbers with fallback);
    // clampedMin/clampedMax: the range of [ReflectRange], when specified.
    public static uint GetUnsignedLong(Element element, string name, bool positive, uint? defaultValue, long? clampedMin = null, long clampedMax = MaxUnsigned)
    {
        var minimum = positive ? 1L : 0L;
        if (clampedMin is { } rangeMin)
        {
            minimum = rangeMin;
        }

        var maximum = clampedMin is null ? MaxUnsigned : clampedMax;
        var value = element.GetAttributeValue(name);
        if (value is not null && Microsyntaxes.TryParseNonNegativeInteger(value, out var parsed))
        {
            if (parsed >= minimum && parsed <= maximum)
                return (uint)parsed;

            if (clampedMin is not null)
                return (uint)(parsed < minimum ? minimum : maximum);
        }

        if (defaultValue is { } defaultResult)
            return defaultResult;

        return (uint)minimum;
    }

    // positive: limited to only positive numbers (throws for 0); positiveWithFallback: limited to only positive numbers with fallback.
    public static void SetUnsignedLong(Element element, string name, bool positive, bool positiveWithFallback, uint? defaultValue, uint value)
    {
        if (positive && value == 0)
            throw DomException.IndexSize("The value must be positive");

        var minimum = positive || positiveWithFallback ? 1u : 0u;
        var newValue = defaultValue ?? minimum;
        if (value >= minimum && value <= MaxUnsigned)
        {
            newValue = value;
        }

        element.SetAttributeValue(name, newValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // double

    public static double GetDouble(Element element, string name, bool positive, double? defaultValue)
    {
        var value = element.GetAttributeValue(name);
        if (value is not null && Microsyntaxes.TryParseFloat(value, out var parsed))
        {
            if (parsed > 0 || !positive)
                return parsed;
        }

        return defaultValue ?? 0;
    }

    public static void SetDouble(Element element, string name, bool positive, double value)
    {
        if (!double.IsFinite(value))
            throw new TypeErrorException("The value must be finite");

        if (positive && !(value > 0))
            return;

        element.SetAttributeValue(name, Microsyntaxes.ToBestFloatingPointRepresentation(value));
    }

    // DOMTokenList

    public static DomTokenList GetTokenList(Element element, string name, IReadOnlyCollection<string>? supportedTokens = null)
    {
        var tokenLists = element.ElementRareData.TokenLists ??= new Dictionary<string, DomTokenList>(StringComparer.Ordinal);
        if (!tokenLists.TryGetValue(name, out var tokenList))
        {
            tokenList = new DomTokenList(element, name, supportedTokens);
            tokenLists[name] = tokenList;
        }

        return tokenList;
    }

    // Element? (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#attr-associated-element)

    public static T? GetElement<T>(Element element, string name)
        where T : Element
    {
        var explicitlySet = element.RareDataOrNull is ElementRareData rareData && rareData.ExplicitlySetAttrElements?.TryGetValue(name, out var reference) == true
            ? reference
            : null;
        if (explicitlySet is not null)
        {
            if (!explicitlySet.TryGetTarget(out var target))
                return null;

            // It must be a descendant of one of the shadow-including ancestors of the element
            return IsDescendantOfShadowIncludingAncestor(element, target) ? target as T : null;
        }

        var value = element.GetAttributeValue(name);
        if (value is null)
            return null;

        var root = element.GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is T candidate && candidate.IdValue == value)
                return candidate;
        }

        return null;
    }

    public static void SetElement(Element element, string name, Element? value)
    {
        if (value is null)
        {
            element.ElementRareData.ExplicitlySetAttrElements?.Remove(name);
            element.RemoveAttributeByLocalName(name);
            return;
        }

        element.SetAttributeValue(name, "");
        (element.ElementRareData.ExplicitlySetAttrElements ??= new Dictionary<string, WeakReference<Element>>(StringComparer.Ordinal))[name] = new WeakReference<Element>(value);
    }

    // FrozenArray<Element>? (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#attr-associated-elements)

    public static IReadOnlyList<Element>? GetElements(Element element, string name)
    {
        var elements = GetAttrAssociatedElements(element, name);
        if (elements is null)
        {
            (element.RareDataOrNull as ElementRareData)?.CachedAttrAssociatedElements?.Remove(name);
            return null;
        }

        // Return the same object while the elements are the same
        var cache = element.ElementRareData.CachedAttrAssociatedElements ??= new Dictionary<string, Element[]>(StringComparer.Ordinal);
        if (cache.TryGetValue(name, out var cached) && cached.AsSpan().SequenceEqual(elements.ToArray()))
            return cached;

        var result = elements.ToArray();
        cache[name] = result;
        return result;
    }

    private static List<Element>? GetAttrAssociatedElements(Element element, string name)
    {
        if (element.RareDataOrNull is ElementRareData { ExplicitlySetAttrElementLists: { } lists } && lists.TryGetValue(name, out var references))
        {
            var result = new List<Element>();
            foreach (var reference in references)
            {
                if (reference.TryGetTarget(out var target) && IsDescendantOfShadowIncludingAncestor(element, target))
                {
                    result.Add(target);
                }
            }

            return result;
        }

        var value = element.GetAttributeValue(name);
        if (value is null)
            return null;

        var elements = new List<Element>();
        var root = element.GetRoot();
        foreach (var id in InfraString.SplitOnAsciiWhitespace(value))
        {
            for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
            {
                if (node is Element candidate && candidate.IdValue == id)
                {
                    elements.Add(candidate);
                    break;
                }
            }
        }

        return elements;
    }

    public static void SetElements(Element element, string name, IReadOnlyList<Element>? value)
    {
        if (value is null)
        {
            (element.RareDataOrNull as ElementRareData)?.ExplicitlySetAttrElementLists?.Remove(name);
            element.RemoveAttributeByLocalName(name);
            return;
        }

        element.SetAttributeValue(name, "");
        var references = new List<WeakReference<Element>>(value.Count);
        foreach (var item in value)
        {
            references.Add(new WeakReference<Element>(item));
        }

        (element.ElementRareData.ExplicitlySetAttrElementLists ??= new Dictionary<string, List<WeakReference<Element>>>(StringComparer.Ordinal))[name] = references;
    }

    private static bool IsDescendantOfShadowIncludingAncestor(Element element, Element target)
    {
        for (Node? ancestor = element; ancestor is not null; ancestor = ancestor.ParentNode is { } parent ? parent : (ancestor as ShadowRoot)?.Host)
        {
            if (ancestor.IsAncestorOf(target))
                return true;
        }

        return false;
    }

    /// <summary>The attribute change steps of the element reflection: changing the content attribute clears the explicitly set element(s).</summary>
    public static void OnAttributeChanged(Element element, string localName)
    {
        if (element.RareDataOrNull is ElementRareData rareData)
        {
            rareData.ExplicitlySetAttrElements?.Remove(localName);
            rareData.ExplicitlySetAttrElementLists?.Remove(localName);
        }
    }
}

internal static partial class Reflection
{
    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-tabindex</summary>
    public static int GetTabIndex(Element element)
    {
        var value = element.GetAttributeValue("tabindex");
        if (value is not null && Microsyntaxes.TryParseInteger(value, out var parsed) && parsed is >= int.MinValue and <= int.MaxValue)
            return (int)parsed;

        if (element.IsHtmlNamespace && element.TagId is TagId.A or TagId.Area or TagId.Button or TagId.Frame or TagId.Iframe or TagId.Input or TagId.Object or TagId.Select or TagId.Textarea)
            return 0;

        if (element is SvgAElement or MathMLAnchorElement)
            return 0;

        if (element.IsHtml(TagId.Summary) && IsSummaryForItsParentDetails(element))
            return 0;

        return -1;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#summary-for-its-parent-details</summary>
    public static bool IsSummaryForItsParentDetails(Element summary)
    {
        if (summary.ParentNode is not Element parent || !parent.IsHtml(TagId.Details))
            return false;

        for (var child = parent.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child.IsHtml(TagId.Summary))
                return ReferenceEquals(child, summary);
        }

        return false;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#encoding-parsing-and-serializing-a-url: parses the URL
    /// relative to the document and serializes it, or returns null on failure.
    /// </summary>
    public static string? EncodingParseAndSerializeUrl(Document document, string url) => EncodingParseUrl(document, url)?.Serialize();

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#encoding-parsing-a-url: parses the URL relative to the document, or returns null on failure.</summary>
    public static Urls.UrlRecord? EncodingParseUrl(Document document, string url) => UrlParser.Parse(url, document.DocumentBaseUrl, WebEncodingRegistryHelper.GetUrlEncoding(document));


    /// <summary>The getter of the action and formAction IDL attributes (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fs-action).</summary>
    public static string GetFormAction(Element element, string name)
    {
        var value = element.GetAttributeValue(name);
        if (string.IsNullOrEmpty(value))
            return element.NodeDocument.Url;

        return EncodingParseAndSerializeUrl(element.NodeDocument, value) ?? InfraString.ToScalarValueString(value);
    }

    /// <summary>
    /// The width or height of the dimensions of an img or input element (https://html.spec.whatwg.org/multipage/embedded-content-other.html#determine-dimensions).
    /// Elements are never rendered and images are never available, so the dimensions come from the attributes.
    /// </summary>
    public static uint GetDimension(Element element, string name)
    {
        var value = element.GetAttributeValue(name);
        if (value is not null && Microsyntaxes.TryParseNonNegativeInteger(value, out var parsed))
            return (uint)Math.Min(parsed, uint.MaxValue);

        return 0;
    }

    /// <summary>The href getter of HTMLHyperlinkElementUtils (https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-href).</summary>
    public static string GetHyperlinkHref(Element element)
    {
        var value = element.GetAttributeValue("href");
        if (value is null)
            return "";

        return EncodingParseAndSerializeUrl(element.NodeDocument, value) ?? value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fe-autocomplete</summary>
    public static string GetIdlExposedAutofillValue(Element element)
    {
        // The complete autofill processing model is part of the forms implementation; the value is the normalized
        // content attribute when it is a single token.
        var value = element.GetAttributeValue("autocomplete");
        if (value is null)
            return "";

        var tokens = InfraString.SplitOnAsciiWhitespace(value);
        return tokens.Count == 1 ? InfraAscii.ToAsciiLowercase(tokens[0]) : "";
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#get-html-aware-text-content</summary>
    public static string GetHtmlAwareTextContent(Element element, bool includeAltText)
    {
        var text = new System.Text.StringBuilder();
        var node = element.FirstChild;
        while (node is not null)
        {
            if (node is Element descendant && descendant.LocalName == "script" && (descendant.IsHtmlNamespace || ReferenceEquals(descendant.NamespaceUri, Namespaces.Svg)))
            {
                node = node.GetNextInTreeOrderSkippingChildren(element);
                continue;
            }

            if (node is Text textNode)
            {
                text.Append(textNode.Data);
            }
            else if (node is Element image && image.IsHtml(TagId.Img))
            {
                if (includeAltText && image.GetAttributeValue("alt") is { } alt)
                {
                    text.Append(alt);
                }

                node = node.GetNextInTreeOrderSkippingChildren(element);
                continue;
            }

            node = node.GetNextInTreeOrder(element);
        }

        return InfraString.StripAndCollapseAsciiWhitespace(text.ToString());
    }
}

internal static class WebEncodingRegistryHelper
{
    /// <summary>The encoding used to encode the query of URLs (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#encoding-parsing-a-url).</summary>
    public static Encodings.WebEncodingId GetUrlEncoding(Document document) => Encodings.WebEncodingRegistry.GetOutputEncoding(document.Encoding);
}
