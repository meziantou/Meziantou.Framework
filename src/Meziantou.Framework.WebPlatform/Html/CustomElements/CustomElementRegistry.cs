using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/custom-elements.html#customelementregistry: defines custom elements with .NET
/// constructors (see <see cref="CustomElementConstructor"/>).
/// </summary>
/// <remarks>
/// The document of a <see cref="BrowsingContext"/> has a global registry (<see cref="Window.CustomElements"/>). Other
/// documents (parsed without a browsing context, <c>DOMParser</c>, <c>createHTMLDocument()</c>...) have no registry: use a
/// scoped registry (<c>new CustomElementRegistry()</c>) and <see cref="Initialize(Node)"/>, or
/// <see cref="HtmlParserOptions.CustomElementRegistry"/>.
/// </remarks>
/// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#customelementregistry"/>
[IdlInterface("CustomElementRegistry", "https://html.spec.whatwg.org/multipage/custom-elements.html#customelementregistry")]
public sealed class CustomElementRegistry
{
    /// <summary>The "default" registry argument of "create an element" (the registry of the document).</summary>
    internal static readonly CustomElementRegistry Default = new(document: null);

    private static volatile bool s_hasDefinitions;

    private readonly Dictionary<string, CustomElementDefinition> _definitionsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<CustomElementConstructor, CustomElementDefinition> _definitionsByConstructor = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, TaskCompletionSource<CustomElementConstructor>> _whenDefinedPromises = new(StringComparer.Ordinal);
    private readonly List<WeakReference<Document>> _scopedDocuments = [];
    private readonly Document? _document;
    private bool _elementDefinitionIsRunning;

    /// <summary>Creates a scoped registry (https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry")]
    public CustomElementRegistry()
    {
        IsScoped = true;
    }

    /// <summary>Creates the global registry of a document of a browsing context.</summary>
    internal CustomElementRegistry(Document? document)
    {
        _document = document;
    }

    /// <summary>Whether a custom element was ever defined (in any registry): until then, no reaction can be enqueued.</summary>
    internal static bool HasDefinitions => s_hasDefinitions;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#is-scoped</summary>
    [NotInIdl("Whether the registry is a scoped registry (created by the constructor)")]
    public bool IsScoped { get; }

    /// <summary>Whether the registry has definitions (used to skip the lookups).</summary>
    internal bool HasAnyDefinition => _definitionsByName.Count > 0;

    /// <summary>https://dom.spec.whatwg.org/#is-a-global-custom-element-registry</summary>
    internal static bool IsGlobal(CustomElementRegistry? registry) => registry is { IsScoped: false };

    /// <summary>https://dom.spec.whatwg.org/#effective-global-custom-element-registry</summary>
    internal static CustomElementRegistry? GetEffectiveGlobal(CustomElementRegistry? registry) => IsGlobal(registry) ? registry : null;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#look-up-a-custom-element-registry</summary>
    internal static CustomElementRegistry? LookUp(Node node) => node switch
    {
        Element element => element.CustomElementRegistry,
        ShadowRoot shadowRoot => shadowRoot.CustomElementRegistryStorage,
        Document document => document.CustomElementRegistryStorage,
        _ => null,
    };

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#look-up-a-custom-element-definition</summary>
    internal static CustomElementDefinition? LookUpDefinition(CustomElementRegistry? registry, string? @namespace, string localName, string? isValue)
    {
        if (registry is null || registry._definitionsByName.Count == 0 || !ReferenceEquals(@namespace, Namespaces.Html))
            return null;

        if (registry._definitionsByName.TryGetValue(localName, out var definition) && definition.LocalName == localName)
            return definition;

        if (isValue is not null && registry._definitionsByName.TryGetValue(isValue, out definition) && definition.LocalName == localName)
            return definition;

        return null;
    }

    /// <summary>Returns the definition whose constructor is <paramref name="constructor"/>, or null.</summary>
    internal CustomElementDefinition? GetDefinition(CustomElementConstructor constructor) => _definitionsByConstructor.GetValueOrDefault(constructor);

    /// <summary>Adds a document to the scoped document set (https://html.spec.whatwg.org/multipage/custom-elements.html#scoped-document-set).</summary>
    internal void AddScopedDocument(Document document)
    {
        foreach (var reference in _scopedDocuments)
        {
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, document))
                return;
        }

        _scopedDocuments.RemoveAll(r => !r.TryGetTarget(out _));
        _scopedDocuments.Add(new WeakReference<Document>(document));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-define</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-define"/>
    [IdlMember("define", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-define")]
    public void Define(string name, CustomElementConstructor constructor, ElementDefinitionOptions? options = null)
    {
        using var scope = CEReactionsScope.Enter();
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(constructor);
        if (!constructor.IsConstructor)
            throw new TypeErrorException("The custom element constructor is not a constructor");

        if (!CustomElements.IsValidCustomElementName(name))
            throw DomException.Syntax($"'{name}' is not a valid custom element name");

        if (_definitionsByName.ContainsKey(name))
            throw DomException.NotSupported($"'{name}' is already defined");

        if (_definitionsByConstructor.ContainsKey(constructor))
            throw DomException.NotSupported("The constructor is already used by another custom element definition");

        var localName = name;
        var extends = options?.Extends;
        if (extends is not null)
        {
            if (IsScoped)
                throw DomException.NotSupported("A scoped registry cannot define customized built-in elements");

            if (CustomElements.IsValidCustomElementName(extends))
                throw DomException.NotSupported("A customized built-in element cannot extend a custom element");

            if (ElementFactory.IsUnknownHtmlElement(extends))
                throw DomException.NotSupported($"'{extends}' is not an HTML element");

            localName = extends;
        }

        if (_elementDefinitionIsRunning)
            throw DomException.NotSupported("Custom elements cannot be defined while another custom element is being defined");

        _elementDefinitionIsRunning = true;
        CustomElementDefinitionData data;
        try
        {
            using (AuthorCodeScope.Enter())
            {
                data = constructor.GetDefinitionData();
            }
        }
        finally
        {
            _elementDefinitionIsRunning = false;
        }

        var definition = new CustomElementDefinition(name, localName, constructor, data);
        _definitionsByName.Add(name, definition);
        _definitionsByConstructor.Add(constructor, definition);
        s_hasDefinitions = true;

        // The scope was entered before any definition existed: the reactions of the upgrades need an element queue
        using var upgradeScope = scope.IsActive ? default : CEReactionsScope.Enter();
        if (IsScoped)
        {
            foreach (var reference in _scopedDocuments.ToArray())
            {
                if (reference.TryGetTarget(out var document))
                {
                    UpgradeParticularElements(document, definition, localName, name);
                }
            }
        }
        else if (GetAssociatedDocument() is { } document)
        {
            UpgradeParticularElements(document, definition, localName, name);
        }

        if (_whenDefinedPromises.Remove(name, out var promise))
        {
            promise.SetResult(constructor);
        }
    }

    /// <summary>The associated document of the relevant global object of a global registry.</summary>
    private Document? GetAssociatedDocument() => _document?.RelevantWindow?.Document ?? _document;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#upgrade-particular-elements-within-a-document</summary>
    private void UpgradeParticularElements(Document document, CustomElementDefinition definition, string localName, string name)
    {
        var candidates = new List<Element>();
        foreach (var node in document.ShadowIncludingInclusiveDescendants())
        {
            if (node is Element element && element.LocalName == localName && element.IsHtmlNamespace && ReferenceEquals(element.CustomElementRegistry, this) &&
                (name == localName || element.IsValue == name))
            {
                candidates.Add(element);
            }
        }

        foreach (var element in candidates)
        {
            CustomElementReactions.EnqueueUpgradeReaction(element, definition);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-get</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-get"/>
    [IdlMember("get", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-get")]
    public CustomElementConstructor? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _definitionsByName.TryGetValue(name, out var definition) ? definition.Constructor : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-getname</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-getname"/>
    [IdlMember("getName", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-getname")]
    public string? GetName(CustomElementConstructor constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);
        return _definitionsByConstructor.TryGetValue(constructor, out var definition) ? definition.Name : null;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-whendefined. The task completes
    /// synchronously when the element is defined, like the promise is resolved by <c>define()</c>.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-whendefined"/>
    [IdlMember("whenDefined", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-whendefined")]
    public Task<CustomElementConstructor> WhenDefined(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!CustomElements.IsValidCustomElementName(name))
            return Task.FromException<CustomElementConstructor>(DomException.Syntax($"'{name}' is not a valid custom element name"));

        if (_definitionsByName.TryGetValue(name, out var definition))
            return Task.FromResult(definition.Constructor);

        if (!_whenDefinedPromises.TryGetValue(name, out var promise))
        {
            promise = new TaskCompletionSource<CustomElementConstructor>();
            _whenDefinedPromises.Add(name, promise);
        }

        return promise.Task;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-upgrade</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-upgrade"/>
    [IdlMember("upgrade", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-upgrade")]
    public void Upgrade(Node root)
    {
        using var scope = CEReactionsScope.Enter();
        ArgumentNullException.ThrowIfNull(root);
        foreach (var candidate in root.ShadowIncludingInclusiveDescendants().ToList())
        {
            if (candidate is Element element && ReferenceEquals(element.CustomElementRegistry, this))
            {
                CustomElementReactions.TryUpgrade(element);
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-initialize</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-initialize"/>
    [IdlMember("initialize", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-initialize")]
    public void Initialize(Node root)
    {
        using var scope = CEReactionsScope.Enter();
        ArgumentNullException.ThrowIfNull(root);
        if (!IsScoped && (root is Document || !ReferenceEquals(root.NodeDocument.CustomElementRegistryStorage, this)))
            throw DomException.NotSupported("A global registry can only initialize the nodes of its document");

        if (root is Document { CustomElementRegistryStorage: null } rootDocument)
        {
            rootDocument.CustomElementRegistryStorage = this;
        }
        else if (root is ShadowRoot { CustomElementRegistryStorage: null } shadowRoot)
        {
            shadowRoot.CustomElementRegistryStorage = this;
        }

        for (Node? node = root; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is not Element element)
                continue;

            if (element.CustomElementRegistry is null)
            {
                element.CustomElementRegistry = this;
                if (IsScoped)
                {
                    AddScopedDocument(element.NodeDocument);
                }
            }

            if (!ReferenceEquals(element.CustomElementRegistry, this))
                continue;

            CustomElementReactions.TryUpgrade(element);
        }
    }

    /// <summary>
    /// The steps of the HTML element constructors (https://html.spec.whatwg.org/multipage/dom.html#html-element-constructors)
    /// once the definition is known: creates a new element when the construction stack is empty (e.g. <c>createElement()</c>),
    /// or returns the element being upgraded.
    /// </summary>
    internal static HtmlElement RunHtmlElementConstructor(CustomElementDefinition definition, Document document, CustomElementRegistry? registry)
    {
        var stack = definition.ConstructionStack;
        if (stack.Count == 0)
            return CreateConstructedElement(definition, document, registry);

        var element = stack[^1] ?? throw new TypeErrorException("The custom element is already constructed");
        stack[^1] = null;
        return (HtmlElement)element;
    }

    /// <summary>Creates the element of an HTML element constructor when the construction stack is empty (step 9).</summary>
    internal static HtmlElement CreateConstructedElement(CustomElementDefinition definition, Document document, CustomElementRegistry? registry)
    {
        var element = ElementFactory.CreateHtmlElementForInterface(document, definition.LocalName);
        element.CustomElementRegistry = registry;
        element.CustomElementState = CustomElementState.Custom;
        element.CustomElementDefinition = definition;
        element.IsValue = definition.IsAutonomous ? null : definition.Name;
        return element;
    }
}
