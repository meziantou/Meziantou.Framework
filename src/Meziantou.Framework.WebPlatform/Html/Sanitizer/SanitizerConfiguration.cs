using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>A canonical element or attribute name of a sanitizer configuration: the name and the namespace.</summary>
internal readonly record struct SanitizerName(string Name, string? Namespace);

/// <summary>What the sanitization does with an element (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitize-core).</summary>
internal enum SanitizerElementAction : byte
{
    Keep,
    Remove,
    ReplaceWithChildren,
}

/// <summary>A canonical element of the "elements" list of a sanitizer configuration, with its local attribute lists.</summary>
internal sealed class SanitizerElementEntry(SanitizerName name)
{
    public SanitizerName Name { get; } = name;

    public List<SanitizerName>? Attributes { get; set; }

    public List<SanitizerName>? RemoveAttributes { get; set; }

    public SanitizerElementEntry Clone() => new(Name)
    {
        Attributes = Attributes is null ? null : [.. Attributes],
        RemoveAttributes = RemoveAttributes is null ? null : [.. RemoveAttributes],
    };

    /// <summary>The dictionaries are equal when all their members are equal.</summary>
    public bool IsEqualTo(SanitizerElementEntry other) => Name == other.Name && ListEquals(Attributes, other.Attributes) && ListEquals(RemoveAttributes, other.RemoveAttributes);

    private static bool ListEquals(List<SanitizerName>? left, List<SanitizerName>? right) => left is null ? right is null : right is not null && left.SequenceEqual(right);
}

/// <summary>
/// A canonical sanitizer configuration (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer-config).
/// A list or a boolean that is null does not exist.
/// </summary>
internal sealed class SanitizerConfiguration
{
    public List<SanitizerElementEntry>? Elements { get; set; }

    public List<SanitizerName>? RemoveElements { get; set; }

    public List<SanitizerName>? ReplaceWithChildrenElements { get; set; }

    public List<string>? ProcessingInstructions { get; set; }

    public List<string>? RemoveProcessingInstructions { get; set; }

    public List<SanitizerName>? Attributes { get; set; }

    public List<SanitizerName>? RemoveAttributes { get; set; }

    public bool? Comments { get; set; }

    public bool? DataAttributes { get; set; }

    public bool? JavaScriptUrls { get; set; }

    public SanitizerConfiguration Clone() => new()
    {
        Elements = Elements?.ConvertAll(e => e.Clone()),
        RemoveElements = RemoveElements is null ? null : [.. RemoveElements],
        ReplaceWithChildrenElements = ReplaceWithChildrenElements is null ? null : [.. ReplaceWithChildrenElements],
        ProcessingInstructions = ProcessingInstructions is null ? null : [.. ProcessingInstructions],
        RemoveProcessingInstructions = RemoveProcessingInstructions is null ? null : [.. RemoveProcessingInstructions],
        Attributes = Attributes is null ? null : [.. Attributes],
        RemoveAttributes = RemoveAttributes is null ? null : [.. RemoveAttributes],
        Comments = Comments,
        DataAttributes = DataAttributes,
        JavaScriptUrls = JavaScriptUrls,
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Built-in configurations (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitization-constants)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The built-in safe default configuration, as a new canonical configuration.</summary>
    public static SanitizerConfiguration CreateDefault() => new()
    {
        Elements = [.. SanitizerDefaults.DefaultElements.Select(e => new SanitizerElementEntry(new SanitizerName(e.Name, e.Namespace)) { Attributes = [.. e.Attributes.Select(a => new SanitizerName(a, Namespace: null))] })],
        ProcessingInstructions = [],
        Attributes = [.. SanitizerDefaults.DefaultAttributes.Select(a => new SanitizerName(a, Namespace: null))],
        Comments = false,
        DataAttributes = false,
        JavaScriptUrls = false,
    };

    private static readonly HashSet<SanitizerName> NonReplaceableElements = [.. SanitizerDefaults.NonReplaceableElements.Select(e => new SanitizerName(e.Name, e.Namespace))];

    private static readonly HashSet<(SanitizerName Element, SanitizerName Attribute)> NavigatingUrlAttributes =
        [.. SanitizerDefaults.NavigatingUrlAttributes.Select(e => (new SanitizerName(e.Element, e.ElementNamespace), new SanitizerName(e.Attribute, e.AttributeNamespace)))];

    private static readonly HashSet<(SanitizerName Element, SanitizerName Attribute)> AnimatingUrlAttributes =
        [.. SanitizerDefaults.AnimatingUrlAttributes.Select(e => (new SanitizerName(e.Element, e.ElementNamespace), new SanitizerName(e.Attribute, Namespace: null)))];

    /// <summary>The event handler content attributes (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-content-attributes).</summary>
    private static readonly string[] EventHandlerAttributes = [.. EventHandlerNames.Elements.Union(EventHandlerNames.WindowReflecting).Select(name => "on" + name).Order(StringComparer.Ordinal)];

    // ----------------------------------------------------------------------------------------------------------------
    // Canonicalization (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#canonicalize-the-configuration)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#canonicalize-a-sanitizer-name</summary>
    private static SanitizerName CanonicalizeName(string name, string? ns, string? defaultNamespace, bool isString)
    {
        if (isString)
            return new SanitizerName(name, defaultNamespace);

        return new SanitizerName(name, string.IsNullOrEmpty(ns) ? null : ns);
    }

    public static SanitizerName CanonicalizeElement(SanitizerElement element) => element.Value switch
    {
        string name => CanonicalizeName(name, ns: null, Namespaces.Html, isString: true),
        SanitizerElementNamespace dictionary => CanonicalizeName(dictionary.Name, dictionary.Namespace, Namespaces.Html, isString: false),
        _ => throw new TypeErrorException("The element is not valid"),
    };

    public static SanitizerName CanonicalizeAttribute(SanitizerAttribute attribute) => attribute.Value switch
    {
        string name => CanonicalizeName(name, ns: null, defaultNamespace: null, isString: true),
        SanitizerAttributeNamespace dictionary => CanonicalizeName(dictionary.Name, dictionary.Namespace, defaultNamespace: null, isString: false),
        _ => throw new TypeErrorException("The attribute is not valid"),
    };

    public static string CanonicalizeProcessingInstruction(SanitizerPI pi) => pi.Value switch
    {
        string target => target,
        SanitizerProcessingInstruction dictionary => dictionary.Target,
        _ => throw new TypeErrorException("The processing instruction is not valid"),
    };

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#canonicalize-a-sanitizerelementwithattributes</summary>
    public static SanitizerElementEntry CanonicalizeElementWithAttributes(SanitizerElementWithAttributes element)
    {
        SanitizerElementEntry result;
        switch (element.Value)
        {
            case string name:
                result = new SanitizerElementEntry(CanonicalizeName(name, ns: null, Namespaces.Html, isString: true));
                break;
            case SanitizerElementNamespaceWithAttributes dictionary:
                result = new SanitizerElementEntry(CanonicalizeName(dictionary.Name, dictionary.Namespace, Namespaces.Html, isString: false))
                {
                    Attributes = dictionary.Attributes?.Select(CanonicalizeAttribute).ToList(),
                    RemoveAttributes = dictionary.RemoveAttributes?.Select(CanonicalizeAttribute).ToList(),
                };
                break;
            default:
                throw new TypeErrorException("The element is not valid");
        }

        if (result.Attributes is null && result.RemoveAttributes is null)
        {
            result.RemoveAttributes = [];
        }

        return result;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#canonicalize-the-configuration</summary>
    public static SanitizerConfiguration Canonicalize(SanitizerConfig config, bool permissiveDefaults)
    {
        var result = new SanitizerConfiguration
        {
            Elements = config.Elements?.Select(CanonicalizeElementWithAttributes).ToList(),
            RemoveElements = config.RemoveElements?.Select(CanonicalizeElement).ToList(),
            ReplaceWithChildrenElements = config.ReplaceWithChildrenElements?.Select(CanonicalizeElement).ToList(),
            ProcessingInstructions = config.ProcessingInstructions?.Select(CanonicalizeProcessingInstruction).ToList(),
            RemoveProcessingInstructions = config.RemoveProcessingInstructions?.Select(CanonicalizeProcessingInstruction).ToList(),
            Attributes = config.Attributes?.Select(CanonicalizeAttribute).ToList(),
            RemoveAttributes = config.RemoveAttributes?.Select(CanonicalizeAttribute).ToList(),
            Comments = config.Comments,
            DataAttributes = config.DataAttributes,
            JavaScriptUrls = config.JavaScriptUrls,
        };

        result.ApplyDefaults(permissiveDefaults);
        return result;
    }

    /// <summary>The steps of "canonicalize the configuration" that add the missing members.</summary>
    public void ApplyDefaults(bool permissiveDefaults)
    {
        if (Elements is null && RemoveElements is null)
        {
            RemoveElements = [];
        }

        if (Attributes is null && RemoveAttributes is null)
        {
            RemoveAttributes = [];
        }

        if (ProcessingInstructions is null && RemoveProcessingInstructions is null)
        {
            if (permissiveDefaults)
            {
                RemoveProcessingInstructions = [];
            }
            else
            {
                ProcessingInstructions = [];
            }
        }

        Comments ??= permissiveDefaults;
        if (Attributes is not null)
        {
            DataAttributes ??= permissiveDefaults;
        }

        JavaScriptUrls ??= permissiveDefaults;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Validity (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizerconfig-valid)
    // ----------------------------------------------------------------------------------------------------------------

    private static bool HasDuplicates<T>(List<T> list) => list.Distinct().Count() != list.Count;

    private static bool Intersects(List<SanitizerName>? left, List<SanitizerName>? right) => left is not null && right is not null && left.Exists(right.Contains);

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#custom-data-attribute</summary>
    public static bool IsCustomDataAttribute(SanitizerName attribute)
    {
        if (attribute.Namespace is not null || attribute.Name.Length <= 5 || !attribute.Name.StartsWith("data-", StringComparison.Ordinal))
            return false;

        foreach (var c in attribute.Name)
        {
            if (char.IsAsciiLetterUpper(c))
                return false;
        }

        return true;
    }

    public bool IsValid()
    {
        if (Elements is not null && RemoveElements is not null)
            return false;

        if (ProcessingInstructions is not null && RemoveProcessingInstructions is not null)
            return false;

        if (Attributes is not null && RemoveAttributes is not null)
            return false;

        if (Elements is not null)
        {
            if (HasDuplicates(Elements.ConvertAll(e => e.Name)))
                return false;
        }
        else if (RemoveElements is not null && HasDuplicates(RemoveElements))
        {
            return false;
        }

        if (ReplaceWithChildrenElements is not null && HasDuplicates(ReplaceWithChildrenElements))
            return false;

        if (ProcessingInstructions is not null)
        {
            if (HasDuplicates(ProcessingInstructions))
                return false;
        }
        else if (RemoveProcessingInstructions is not null && HasDuplicates(RemoveProcessingInstructions))
        {
            return false;
        }

        if (Attributes is not null)
        {
            if (HasDuplicates(Attributes))
                return false;
        }
        else if (RemoveAttributes is not null && HasDuplicates(RemoveAttributes))
        {
            return false;
        }

        if (ReplaceWithChildrenElements is not null)
        {
            if (ReplaceWithChildrenElements.Exists(NonReplaceableElements.Contains))
                return false;

            if (Elements is not null ? Elements.Exists(e => ReplaceWithChildrenElements.Contains(e.Name)) : Intersects(RemoveElements, ReplaceWithChildrenElements))
                return false;
        }

        if (Attributes is not null)
        {
            if (Elements is not null)
            {
                foreach (var element in Elements)
                {
                    if (element.Attributes is not null && HasDuplicates(element.Attributes))
                        return false;

                    if (element.RemoveAttributes is not null && HasDuplicates(element.RemoveAttributes))
                        return false;

                    if (Intersects(Attributes, element.Attributes))
                        return false;

                    if (element.RemoveAttributes is not null && !element.RemoveAttributes.TrueForAll(Attributes.Contains))
                        return false;

                    if (DataAttributes == true && element.Attributes is not null && element.Attributes.Exists(IsCustomDataAttribute))
                        return false;
                }
            }

            if (DataAttributes == true && Attributes.Exists(IsCustomDataAttribute))
                return false;
        }
        else
        {
            if (Elements is not null)
            {
                foreach (var element in Elements)
                {
                    if (element.Attributes is not null && element.RemoveAttributes is not null)
                        return false;

                    if (element.Attributes is not null && HasDuplicates(element.Attributes))
                        return false;

                    if (element.RemoveAttributes is not null && HasDuplicates(element.RemoveAttributes))
                        return false;

                    if (Intersects(RemoveAttributes, element.Attributes))
                        return false;

                    if (Intersects(RemoveAttributes, element.RemoveAttributes))
                        return false;
                }
            }

            if (DataAttributes is not null)
                return false;
        }

        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // get() (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-get)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#compare-sanitizer-items</summary>
    private static int Compare(SanitizerName left, SanitizerName right)
    {
        if (left.Namespace is null)
        {
            if (right.Namespace is not null)
                return -1;
        }
        else
        {
            if (right.Namespace is null)
                return 1;

            var namespaceComparison = string.CompareOrdinal(left.Namespace, right.Namespace);
            if (namespaceComparison != 0)
                return namespaceComparison;
        }

        return string.CompareOrdinal(left.Name, right.Name);
    }

    private static List<SanitizerName> Sorted(List<SanitizerName> list)
    {
        var result = new List<SanitizerName>(list);
        result.Sort(Compare);
        return result;
    }

    private static SanitizerElement ToElement(SanitizerName name) => new SanitizerElementNamespace { Name = name.Name, Namespace = name.Namespace };

    private static SanitizerAttribute ToAttribute(SanitizerName name) => new SanitizerAttributeNamespace { Name = name.Name, Namespace = name.Namespace };

    private static SanitizerPI ToProcessingInstruction(string target) => new SanitizerProcessingInstruction { Target = target };

    /// <summary>Returns the configuration as a sorted SanitizerConfig dictionary.</summary>
    public SanitizerConfig ToSanitizerConfig()
    {
        List<SanitizerElementWithAttributes>? elements = null;
        List<SanitizerElement>? removeElements = null;
        if (Elements is not null)
        {
            var sorted = new List<SanitizerElementEntry>(Elements);
            sorted.Sort((left, right) => Compare(left.Name, right.Name));
            elements = sorted.ConvertAll(e => (SanitizerElementWithAttributes)new SanitizerElementNamespaceWithAttributes
            {
                Name = e.Name.Name,
                Namespace = e.Name.Namespace,
                Attributes = e.Attributes is null ? null : Sorted(e.Attributes).ConvertAll(ToAttribute),
                RemoveAttributes = e.RemoveAttributes is null ? null : Sorted(e.RemoveAttributes).ConvertAll(ToAttribute),
            });
        }
        else
        {
            removeElements = Sorted(RemoveElements ?? []).ConvertAll(ToElement);
        }

        List<SanitizerPI>? processingInstructions = null;
        List<SanitizerPI>? removeProcessingInstructions = null;
        if (ProcessingInstructions is not null)
        {
            processingInstructions = ProcessingInstructions.Order(StringComparer.Ordinal).Select(ToProcessingInstruction).ToList();
        }
        else
        {
            removeProcessingInstructions = (RemoveProcessingInstructions ?? []).Order(StringComparer.Ordinal).Select(ToProcessingInstruction).ToList();
        }

        return new SanitizerConfig
        {
            Elements = elements,
            RemoveElements = removeElements,
            ReplaceWithChildrenElements = ReplaceWithChildrenElements is null ? null : Sorted(ReplaceWithChildrenElements).ConvertAll(ToElement),
            ProcessingInstructions = processingInstructions,
            RemoveProcessingInstructions = removeProcessingInstructions,
            Attributes = Attributes is null ? null : Sorted(Attributes).ConvertAll(ToAttribute),
            RemoveAttributes = Attributes is null ? Sorted(RemoveAttributes ?? []).ConvertAll(ToAttribute) : null,
            Comments = Comments,
            DataAttributes = DataAttributes,
            JavaScriptUrls = JavaScriptUrls,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Modifiers (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#the-sanitizer-interface)
    // ----------------------------------------------------------------------------------------------------------------

    private static bool Remove<T>(List<T>? list, T item) => list is not null && list.RemoveAll(i => EqualityComparer<T>.Default.Equals(i, item)) > 0;

    private static List<SanitizerName> CreateSet(List<SanitizerName> list) => [.. list.Distinct()];

    public SanitizerElementEntry? FindElement(SanitizerName name) => Elements?.Find(e => e.Name == name);

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-allowelement</summary>
    public bool AllowElement(SanitizerElementEntry element)
    {
        if (Elements is not null)
        {
            var modified = Remove(ReplaceWithChildrenElements, element.Name);
            if (Attributes is not null)
            {
                if (element.Attributes is not null)
                {
                    element.Attributes = CreateSet(element.Attributes);
                    element.Attributes.RemoveAll(Attributes.Contains);
                    if (DataAttributes == true)
                    {
                        element.Attributes.RemoveAll(IsCustomDataAttribute);
                    }
                }

                if (element.RemoveAttributes is not null)
                {
                    element.RemoveAttributes = CreateSet(element.RemoveAttributes);
                    element.RemoveAttributes.RemoveAll(a => !Attributes.Contains(a));
                }
            }
            else
            {
                if (element.Attributes is not null)
                {
                    element.Attributes = CreateSet(element.Attributes);
                    var localRemove = element.RemoveAttributes ?? [];
                    element.Attributes.RemoveAll(localRemove.Contains);
                    element.RemoveAttributes = null;
                    element.Attributes.RemoveAll(a => RemoveAttributes?.Contains(a) == true);
                }

                if (element.RemoveAttributes is not null)
                {
                    element.RemoveAttributes = CreateSet(element.RemoveAttributes);
                    element.RemoveAttributes.RemoveAll(a => RemoveAttributes?.Contains(a) == true);
                }
            }

            var current = FindElement(element.Name);
            if (current is null)
            {
                Elements.Add(element);
                return true;
            }

            if (element.IsEqualTo(current))
                return modified;

            Elements.Remove(current);
            Elements.Add(element);
            return true;
        }

        if (element.Attributes is not null || element.RemoveAttributes is { Count: > 0 })
            return false;

        var removed = Remove(ReplaceWithChildrenElements, element.Name);
        if (RemoveElements?.Contains(element.Name) != true)
            return removed;

        Remove(RemoveElements, element.Name);
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer-remove-an-element</summary>
    public bool RemoveElement(SanitizerName element)
    {
        var modified = Remove(ReplaceWithChildrenElements, element);
        if (Elements is not null)
        {
            if (FindElement(element) is { } current)
            {
                Elements.Remove(current);
                return true;
            }

            return modified;
        }

        RemoveElements ??= [];
        if (RemoveElements.Contains(element))
            return modified;

        RemoveElements.Add(element);
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-replaceelementwithchildren</summary>
    public bool ReplaceElementWithChildren(SanitizerName element)
    {
        if (NonReplaceableElements.Contains(element))
            return false;

        var modified = Elements is not null && Elements.RemoveAll(e => e.Name == element) > 0;
        if (Remove(RemoveElements, element))
        {
            modified = true;
        }

        ReplaceWithChildrenElements ??= [];
        if (!ReplaceWithChildrenElements.Contains(element))
        {
            ReplaceWithChildrenElements.Add(element);
            return true;
        }

        return modified;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-allowattribute</summary>
    public bool AllowAttribute(SanitizerName attribute)
    {
        if (Attributes is not null)
        {
            if (DataAttributes == true && IsCustomDataAttribute(attribute))
                return false;

            if (Attributes.Contains(attribute))
                return false;

            if (Elements is not null)
            {
                foreach (var element in Elements)
                {
                    Remove(element.Attributes, attribute);
                }
            }

            Attributes.Add(attribute);
            return true;
        }

        if (RemoveAttributes?.Contains(attribute) != true)
            return false;

        Remove(RemoveAttributes, attribute);
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer-remove-an-attribute</summary>
    public bool RemoveAttribute(SanitizerName attribute)
    {
        if (Attributes is not null)
        {
            var modified = Remove(Attributes, attribute);
            if (Elements is not null)
            {
                foreach (var element in Elements)
                {
                    if (Remove(element.Attributes, attribute))
                    {
                        modified = true;
                    }

                    if (Remove(element.RemoveAttributes, attribute))
                    {
                        modified = true;
                    }
                }
            }

            return modified;
        }

        RemoveAttributes ??= [];
        if (RemoveAttributes.Contains(attribute))
            return false;

        if (Elements is not null)
        {
            foreach (var element in Elements)
            {
                Remove(element.Attributes, attribute);
                Remove(element.RemoveAttributes, attribute);
            }
        }

        RemoveAttributes.Add(attribute);
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-setcomments</summary>
    public bool SetComments(bool allow)
    {
        if (Comments == allow)
            return false;

        Comments = allow;
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-setdataattributes</summary>
    public bool SetDataAttributes(bool allow)
    {
        if (Attributes is null)
            return false;

        if (DataAttributes == allow)
            return false;

        if (allow)
        {
            if (Elements is not null)
            {
                foreach (var element in Elements)
                {
                    element.Attributes?.RemoveAll(IsCustomDataAttribute);
                }
            }

            Attributes.RemoveAll(IsCustomDataAttribute);
        }

        DataAttributes = allow;
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-setjavascripturls</summary>
    public bool SetJavaScriptUrls(bool allow)
    {
        if (JavaScriptUrls == allow)
            return false;

        JavaScriptUrls = allow;
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-allowprocessinginstruction</summary>
    public bool AllowProcessingInstruction(string target)
    {
        if (ProcessingInstructions is not null)
        {
            if (ProcessingInstructions.Contains(target, StringComparer.Ordinal))
                return false;

            ProcessingInstructions.Add(target);
            return true;
        }

        return Remove(RemoveProcessingInstructions, target);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizer-removeprocessinginstruction</summary>
    public bool RemoveProcessingInstruction(string target)
    {
        if (ProcessingInstructions is not null)
            return Remove(ProcessingInstructions, target);

        RemoveProcessingInstructions ??= [];
        if (RemoveProcessingInstructions.Contains(target, StringComparer.Ordinal))
            return false;

        RemoveProcessingInstructions.Add(target);
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizer-remove-unsafe</summary>
    public bool RemoveUnsafe()
    {
        var result = false;
        foreach (var (name, ns) in SanitizerDefaults.UnsafeElements)
        {
            if (RemoveElement(new SanitizerName(name, ns)))
            {
                result = true;
            }
        }

        foreach (var attribute in EventHandlerAttributes)
        {
            if (RemoveAttribute(new SanitizerName(attribute, Namespace: null)))
            {
                result = true;
            }
        }

        if (JavaScriptUrls == true)
        {
            result = true;
            JavaScriptUrls = false;
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Sanitization (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitize)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitize</summary>
    public static void Sanitize(Node node, SanitizerConfiguration configuration, bool safe) => GetEffectiveConfiguration(configuration, safe).SanitizeChildren(node);

    /// <summary>Returns the configuration that sanitizes the nodes: a safe sanitization also removes the unsafe elements and attributes.</summary>
    public static SanitizerConfiguration GetEffectiveConfiguration(SanitizerConfiguration configuration, bool safe)
    {
        // The configuration of the sanitizer is not modified by the sanitization
        if (safe)
        {
            configuration = configuration.Clone();
            configuration.RemoveUnsafe();
        }

        return configuration;
    }

    /// <summary>Returns whether the sanitization keeps the comments.</summary>
    public bool KeepsComments => Comments == true;

    /// <summary>Returns whether the sanitization keeps the processing instructions whose target is <paramref name="target"/>.</summary>
    public bool KeepsProcessingInstruction(string target) =>
        ProcessingInstructions is not null ? ProcessingInstructions.Contains(target, StringComparer.Ordinal) : RemoveProcessingInstructions?.Contains(target, StringComparer.Ordinal) != true;

    /// <summary>Returns whether the sanitization keeps the element, removes it, or replaces it by its children.</summary>
    public SanitizerElementAction GetElementAction(SanitizerName elementName)
    {
        if (ReplaceWithChildrenElements?.Contains(elementName) == true)
            return SanitizerElementAction.ReplaceWithChildren;

        if (Elements is not null ? FindElement(elementName) is null : RemoveElements?.Contains(elementName) == true)
            return SanitizerElementAction.Remove;

        return SanitizerElementAction.Keep;
    }

    /// <summary>Returns whether the sanitization removes an attribute of a kept element.</summary>
    /// <param name="elementName">The name of the element.</param>
    /// <param name="local">The entry of the element in the "elements" list (<see cref="FindElement"/>).</param>
    /// <param name="localName">The local name of the attribute.</param>
    /// <param name="namespaceUri">The namespace of the attribute.</param>
    /// <param name="value">The value of the attribute.</param>
    public bool RemovesAttribute(SanitizerName elementName, SanitizerElementEntry? local, string localName, string? namespaceUri, string value)
    {
        var attrName = new SanitizerName(localName, namespaceUri);
        if (local?.RemoveAttributes?.Contains(attrName) == true)
            return true;

        if (Attributes is not null)
        {
            if (!Attributes.Contains(attrName) && local?.Attributes?.Contains(attrName) != true
                && (!localName.StartsWith("data-", StringComparison.Ordinal) || namespaceUri is not null || DataAttributes != true))
            {
                return true;
            }
        }
        else if (local?.Attributes is not null ? !local.Attributes.Contains(attrName) : RemoveAttributes?.Contains(attrName) == true)
        {
            return true;
        }

        if (JavaScriptUrls != true)
        {
            if (NavigatingUrlAttributes.Contains((elementName, attrName)) && ContainsJavaScriptUrl(value))
                return true;

            if (elementName.Namespace == Namespaces.MathML && localName == "href" && namespaceUri is null or Namespaces.XLink && ContainsJavaScriptUrl(value))
                return true;

            if (AnimatingUrlAttributes.Contains((elementName, attrName)) && value is "href" or "xlink:href")
                return true;
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitize-core</summary>
    private void SanitizeChildren(Node node)
    {
        var modified = false;
        var child = node.FirstChild;
        while (child is not null)
        {
            var next = child.NextSibling;
            switch (child)
            {
                case DocumentType or Text:
                    break;

                case Comment:
                    if (!KeepsComments)
                    {
                        child.RemoveFromParent();
                        modified = true;
                    }

                    break;

                case ProcessingInstruction pi:
                    if (!KeepsProcessingInstruction(pi.Target))
                    {
                        child.RemoveFromParent();
                        modified = true;
                    }

                    break;

                case Element element:
                    modified |= SanitizeElement(node, element);
                    break;
            }

            child = next;
        }

        // The Text nodes that become adjacent when nodes are removed or replaced by their children are merged (as browsers do)
        if (modified)
        {
            MergeAdjacentTextNodes(node);
        }
    }

    private static void MergeAdjacentTextNodes(Node node)
    {
        var child = node.FirstChild;
        while (child is not null)
        {
            if (child is Text text and not CDataSection)
            {
                while (text.NextSibling is Text sibling and not CDataSection)
                {
                    text.AppendData(sibling.Data);
                    sibling.RemoveFromParent();
                }
            }

            child = child.NextSibling;
        }
    }

    /// <summary>Sanitizes an element; returns true when the element is removed or replaced by its children.</summary>
    private bool SanitizeElement(Node parent, Element element)
    {
        var elementName = new SanitizerName(element.LocalName, element.NamespaceUri);
        switch (GetElementAction(elementName))
        {
            case SanitizerElementAction.ReplaceWithChildren:
                SanitizeChildren(element);
                var fragment = new DocumentFragment(parent.NodeDocument);
                while (element.FirstChild is { } innerChild)
                {
                    fragment.AppendChild(innerChild);
                }

                parent.ReplaceChild(fragment, element);
                return true;

            case SanitizerElementAction.Remove:
                element.RemoveFromParent();
                return true;
        }

        if (element is HtmlTemplateElement template)
        {
            SanitizeChildren(template.TemplateContents);
        }

        if (element.AttachedShadowRoot is { } shadowRoot)
        {
            SanitizeChildren(shadowRoot);
        }

        var local = FindElement(elementName);

        // The attributes are processed in order; the list is copied as the attributes are removed while iterating
        var attributes = new (string LocalName, string? NamespaceUri, string Value)[element.AttributeCount];
        for (var i = 0; i < attributes.Length; i++)
        {
            attributes[i] = (element.GetAttributeLocalName(i), element.GetAttributeNamespace(i), element.GetAttributeValue(i));
        }

        foreach (var attribute in attributes)
        {
            if (RemovesAttribute(elementName, local, attribute.LocalName, attribute.NamespaceUri, attribute.Value))
            {
                element.RemoveAttributeNS(attribute.NamespaceUri, attribute.LocalName);
            }
        }

        SanitizeChildren(element);
        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#contains-a-javascript-url</summary>
    private static bool ContainsJavaScriptUrl(string value) => UrlParser.Parse(value, baseUrl: null) is { Scheme: "javascript" };
}
