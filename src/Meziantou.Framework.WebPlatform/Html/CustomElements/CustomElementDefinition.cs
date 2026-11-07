using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-definition</summary>
internal sealed class CustomElementDefinition
{
    /// <summary>The callback of a connectedMoveCallback reaction when the definition has no connectedMoveCallback: disconnectedCallback, then connectedCallback.</summary>
    public static readonly object MoveFallback = new();

    private readonly object?[] _lifecycleCallbacks;
    private readonly HashSet<string> _observedAttributes;

    public CustomElementDefinition(string name, string localName, CustomElementConstructor constructor, CustomElementDefinitionData data)
    {
        Name = name;
        LocalName = localName;
        Constructor = constructor;
        _lifecycleCallbacks = data.LifecycleCallbacks;
        _observedAttributes = new HashSet<string>(data.ObservedAttributes, StringComparer.Ordinal);
        FormAssociated = data.FormAssociated;
        DisableInternals = data.DisableInternals;
        DisableShadow = data.DisableShadow;
    }

    public string Name { get; }

    public string LocalName { get; }

    /// <summary>Whether the definition is for an autonomous custom element (rather than a customized built-in element).</summary>
    public bool IsAutonomous => ReferenceEquals(Name, LocalName) || Name == LocalName;

    public CustomElementConstructor Constructor { get; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/custom-elements.html#concept-custom-element-definition-construction-stack: the
    /// elements being upgraded, or null for the "already constructed" marker.
    /// </summary>
    public List<Element?> ConstructionStack { get; } = [];

    public bool FormAssociated { get; }

    public bool DisableInternals { get; }

    public bool DisableShadow { get; }

    public object? GetCallback(CustomElementCallbackKind kind) => _lifecycleCallbacks[(int)kind];

    public bool ObservesAttribute(string localName) => _observedAttributes.Contains(localName);
}

/// <summary>The data a <see cref="CustomElementConstructor"/> provides when it is defined (the lifecycle callbacks, the observed attributes...).</summary>
internal sealed class CustomElementDefinitionData
{
    public const int CallbackCount = (int)CustomElementCallbackKind.FormStateRestore + 1;

    /// <summary>The lifecycle callbacks, indexed by <see cref="CustomElementCallbackKind"/>: null when the definition has no such callback.</summary>
    public object?[] LifecycleCallbacks { get; } = new object?[CallbackCount];

    public IEnumerable<string> ObservedAttributes { get; set; } = [];

    public bool FormAssociated { get; set; }

    public bool DisableInternals { get; set; }

    public bool DisableShadow { get; set; }
}

/// <summary>The state of a form-associated custom element (https://html.spec.whatwg.org/multipage/custom-elements.html#form-associated-custom-elements).</summary>
internal sealed class FormAssociatedCustomElementData
{
    /// <summary>The form owner the last time it was reset (formAssociatedCallback is enqueued when it changes).</summary>
    public HtmlFormElement? FormOwner { get; set; }

    /// <summary>The disabled state the last time it was computed (formDisabledCallback is enqueued when it changes).</summary>
    public bool Disabled { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#face-submission-value: null, a string, a File, or a list of entries.</summary>
    public object? SubmissionValue { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#face-state</summary>
    public object? State { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#face-validity-flags</summary>
    public ValidityFlags ValidityFlags { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#face-validation-message</summary>
    public string ValidationMessage { get; set; } = "";

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#face-validation-anchor</summary>
    public HtmlElement? ValidationAnchor { get; set; }
}
