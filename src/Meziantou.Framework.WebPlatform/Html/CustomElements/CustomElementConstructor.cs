using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A custom element constructor (https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-constructor),
/// passed to <see cref="CustomElementRegistry.Define(string, CustomElementConstructor, ElementDefinitionOptions?)"/>.
/// </summary>
/// <remarks>
/// In a browser, a custom element is an instance of a class that extends <c>HTMLElement</c>, and an upgrade changes the
/// prototype of an existing element. A .NET object cannot change its type, so the element is always created by the library
/// (an <see cref="HtmlElement"/>, or the element interface of the extended element for a customized built-in element), and
/// the constructor creates a <see cref="CustomElement"/> attached to the element: its virtual methods are the lifecycle
/// callbacks, and <see cref="HtmlElement.CustomElement"/> returns it. The static members of a JavaScript custom element
/// class (<c>observedAttributes</c>, <c>disabledFeatures</c>, <c>formAssociated</c>) are properties of the constructor,
/// read once by <c>Define</c>.
/// </remarks>
[IdlInterface("CustomElementConstructor", "https://html.spec.whatwg.org/multipage/custom-elements.html#customelementconstructor")]
public class CustomElementConstructor
{
    private readonly Func<HtmlElement, CustomElement>? _factory;

    /// <summary>Creates a constructor that attaches the object returned by <paramref name="factory"/> to the elements it constructs.</summary>
    /// <param name="factory">
    /// Creates the <see cref="CustomElement"/> of an element, when the element is created (e.g. <c>createElement()</c>, the
    /// HTML parser) or upgraded. It must pass the element to the constructor of <see cref="CustomElement"/>. As in a
    /// JavaScript constructor, it must not add attributes or children to the element.
    /// </param>
    [NotInIdl("A .NET custom element constructor creates the CustomElement attached to the element")]
    public CustomElementConstructor(Func<HtmlElement, CustomElement> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Creates a constructor whose behavior is provided by a derived class (e.g. a script engine).</summary>
    internal CustomElementConstructor()
    {
    }

    /// <summary>
    /// Gets the names of the attributes whose changes invoke <see cref="CustomElement.AttributeChangedCallback"/>
    /// (the static <c>observedAttributes</c> property of a JavaScript custom element class).
    /// </summary>
    [NotInIdl("The static observedAttributes property of a custom element class")]
    public IReadOnlyCollection<string> ObservedAttributes { get; init; } = [];

    /// <summary>Gets the disabled features: <c>"internals"</c> disables <c>attachInternals()</c>, <c>"shadow"</c> disables <c>attachShadow()</c>.</summary>
    [NotInIdl("The static disabledFeatures property of a custom element class")]
    public IReadOnlyCollection<string> DisabledFeatures { get; init; } = [];

    /// <summary>Gets whether the elements are form-associated custom elements (the static <c>formAssociated</c> property of a JavaScript custom element class).</summary>
    [NotInIdl("The static formAssociated property of a custom element class")]
    public bool FormAssociated { get; init; }

    /// <summary>Whether the constructor can be constructed (https://tc39.es/ecma262/#sec-isconstructor).</summary>
    internal virtual bool IsConstructor => true;

    /// <summary>
    /// Returns the data of the definition: the lifecycle callbacks, the observed attributes, the disabled features and whether
    /// the element is form-associated (https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-define, step 14).
    /// </summary>
    internal virtual CustomElementDefinitionData GetDefinitionData()
    {
        var data = new CustomElementDefinitionData
        {
            ObservedAttributes = ObservedAttributes,
            FormAssociated = FormAssociated,
            DisableInternals = DisabledFeatures.Contains("internals"),
            DisableShadow = DisabledFeatures.Contains("shadow"),
        };

        // The callbacks are virtual methods of CustomElement, so they always exist (the default connectedMoveCallback calls
        // disconnectedCallback and connectedCallback, as a definition without connectedMoveCallback)
        var count = FormAssociated ? CustomElementDefinitionData.CallbackCount : (int)CustomElementCallbackKind.AttributeChanged + 1;
        for (var i = 0; i < count; i++)
        {
            data.LifecycleCallbacks[i] = this;
        }

        return data;
    }

    /// <summary>
    /// Constructs the custom element ("constructing C, with no arguments"), running the steps of the HTML element
    /// constructors (https://html.spec.whatwg.org/multipage/dom.html#html-element-constructors) for this constructor.
    /// </summary>
    internal virtual HtmlElement Construct(CustomElementDefinition definition, Document document, CustomElementRegistry? registry)
    {
        var element = CustomElementRegistry.RunHtmlElementConstructor(definition, document, registry);
        var instance = _factory!(element);
        if (instance is null || !ReferenceEquals(instance.Element, element))
            throw new TypeErrorException("The custom element constructor must create the CustomElement of the element it receives");

        element.CustomElementInstance = instance;
        return element;
    }

    /// <summary>Invokes a lifecycle callback of the definition, with the element as the callback this value.</summary>
    internal virtual void InvokeCallback(Element element, CustomElementCallbackKind kind, object callback, object?[] arguments)
    {
        if (element is not HtmlElement { CustomElementInstance: { } instance })
            return;

        switch (kind)
        {
            case CustomElementCallbackKind.Connected:
                instance.ConnectedCallback();
                break;
            case CustomElementCallbackKind.Disconnected:
                instance.DisconnectedCallback();
                break;
            case CustomElementCallbackKind.ConnectedMove:
                instance.ConnectedMoveCallback();
                break;
            case CustomElementCallbackKind.Adopted:
                instance.AdoptedCallback((Document)arguments[0]!, (Document)arguments[1]!);
                break;
            case CustomElementCallbackKind.AttributeChanged:
                instance.AttributeChangedCallback((string)arguments[0]!, (string?)arguments[1], (string?)arguments[2], (string?)arguments[3]);
                break;
            case CustomElementCallbackKind.FormAssociated:
                instance.FormAssociatedCallback((HtmlFormElement?)arguments[0]);
                break;
            case CustomElementCallbackKind.FormReset:
                instance.FormResetCallback();
                break;
            case CustomElementCallbackKind.FormDisabled:
                instance.FormDisabledCallback((bool)arguments[0]!);
                break;
            case CustomElementCallbackKind.FormStateRestore:
                instance.FormStateRestoreCallback(arguments[0], (string)arguments[1]!);
                break;
        }
    }

    /// <summary>Invokes a callback with "report" as the exception behavior (https://webidl.spec.whatwg.org/#invoke-a-callback-function).</summary>
    internal void InvokeCallbackAndReport(Element element, CustomElementCallbackKind kind, object callback, object?[] arguments)
    {
        try
        {
            using (AuthorCodeScope.Enter())
            {
                InvokeCallback(element, kind, callback, arguments);
            }
        }
#pragma warning disable CA1031 // "report the exception"
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ReportException(ex, element.NodeDocument);
        }
    }

    /// <summary>
    /// Reports an exception thrown by the constructor or a callback (https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception)
    /// to the window of <paramref name="document"/>, when the constructor has no realm.
    /// </summary>
    internal virtual void ReportException(Exception exception, Document? document) => Scripting.ScriptErrors.Report(exception, document?.RelevantWindow);
}

/// <summary>
/// The .NET object of a custom element defined with a <see cref="CustomElementConstructor"/>. The virtual methods are the
/// lifecycle callbacks of the custom element (https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-reactions).
/// </summary>
public abstract class CustomElement
{
    /// <summary>Initializes the custom element of <paramref name="element"/>, the element being created or upgraded.</summary>
    protected CustomElement(HtmlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Element = element;

        // As "this" in a JavaScript constructor, the object is the custom element as soon as the base constructor returns, so
        // the callbacks invoked during the rest of the construction (e.g. when the constructor changes an attribute) use it
        if (element.CustomElementState is CustomElementState.Precustomized or CustomElementState.Custom && element.CustomElementInstance is null)
        {
            element.CustomElementInstance = this;
        }
    }

    /// <summary>Gets the element.</summary>
    public HtmlElement Element { get; }

    /// <summary>Called when the element becomes connected.</summary>
    protected internal virtual void ConnectedCallback()
    {
    }

    /// <summary>Called when the element becomes disconnected.</summary>
    protected internal virtual void DisconnectedCallback()
    {
    }

    /// <summary>Called when the element is moved with <c>moveBefore()</c>. The default implementation calls <see cref="DisconnectedCallback"/>, then <see cref="ConnectedCallback"/>.</summary>
    protected internal virtual void ConnectedMoveCallback()
    {
        DisconnectedCallback();
        ConnectedCallback();
    }

    /// <summary>Called when the element is adopted into another document.</summary>
    protected internal virtual void AdoptedCallback(Document oldDocument, Document newDocument)
    {
    }

    /// <summary>Called when an attribute listed in <see cref="CustomElementConstructor.ObservedAttributes"/> is added, changed, removed or replaced.</summary>
    protected internal virtual void AttributeChangedCallback(string name, string? oldValue, string? newValue, string? @namespace)
    {
    }

    /// <summary>Called when the form owner of a form-associated custom element changes.</summary>
    protected internal virtual void FormAssociatedCallback(HtmlFormElement? form)
    {
    }

    /// <summary>Called when the form owner of a form-associated custom element is reset.</summary>
    protected internal virtual void FormResetCallback()
    {
    }

    /// <summary>Called when the disabled state of a form-associated custom element changes.</summary>
    protected internal virtual void FormDisabledCallback(bool disabled)
    {
    }

    /// <summary>Called to restore the state of a form-associated custom element (<paramref name="mode"/> is <c>"restore"</c> or <c>"autocomplete"</c>). The library never restores states.</summary>
    protected internal virtual void FormStateRestoreCallback(object? state, string mode)
    {
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#elementdefinitionoptions</summary>
[IdlInterface("ElementDefinitionOptions", "https://html.spec.whatwg.org/multipage/custom-elements.html#elementdefinitionoptions")]
public sealed class ElementDefinitionOptions
{
    /// <summary>Gets the local name of the element a customized built-in element extends.</summary>
    [IdlMember("extends", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementdefinitionoptions-extends")]
    public string? Extends { get; init; }
}
