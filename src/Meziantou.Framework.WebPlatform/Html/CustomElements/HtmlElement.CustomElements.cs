using Meziantou.Framework.WebPlatform.Html;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

// Custom elements (https://html.spec.whatwg.org/multipage/custom-elements.html): element internals, custom states and
// form-associated custom elements.
public partial class HtmlElement
{
    /// <summary>Gets the object created by the .NET <see cref="CustomElementConstructor"/> of the element, or null.</summary>
    [NotInIdl("The .NET object of a custom element defined with a .NET CustomElementConstructor")]
    public CustomElement? CustomElement => CustomElementInstance;

    internal CustomElement? CustomElementInstance
    {
        get => (RareDataOrNull as ElementRareData)?.CustomElementInstance;
        set => ElementRareData.CustomElementInstance = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-attachinternals</summary>
    [IdlMember("attachInternals", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-attachinternals")]
    public ElementInternals AttachInternals()
    {
        if (IsValue is not null)
            throw DomException.NotSupported("Customized built-in elements cannot attach internals");

        var definition = Dom.CustomElementRegistry.LookUpDefinition(CustomElementRegistry, NamespaceUri, LocalName, isValue: null)
            ?? throw DomException.NotSupported("The element is not a custom element");

        if (definition.DisableInternals)
            throw DomException.NotSupported("The custom element definition disables the internals");

        if (RareDataOrNull is ElementRareData { AttachedInternals: not null })
            throw DomException.NotSupported("The internals are already attached");

        if (CustomElementState is not (CustomElementState.Precustomized or CustomElementState.Custom))
            throw DomException.NotSupported("The element is not a custom element");

        var internals = new ElementInternals(this);
        ElementRareData.AttachedInternals = internals;
        return internals;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#states-set</summary>
    internal CustomStateSet States => ElementRareData.States ??= new CustomStateSet(this);

    /// <summary>Returns whether the element is in the custom state (https://html.spec.whatwg.org/multipage/custom-elements.html#selector-custom).</summary>
    internal bool HasCustomState(string state) => RareDataOrNull is ElementRareData { States: { } states } && states.Contains(state);

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#internal-content-attribute-map</summary>
    internal Dictionary<string, string> InternalContentAttributeMap => ElementRareData.InternalContentAttributeMap ??= new(StringComparer.Ordinal);

    internal Dictionary<string, string>? InternalContentAttributeMapOrNull => (RareDataOrNull as ElementRareData)?.InternalContentAttributeMap;

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#form-associated-custom-element</summary>
    internal bool IsFormAssociatedCustomElement => TagId == TagId.Unknown && RareDataOrNull is ElementRareData { CustomElementDefinition: { FormAssociated: true, IsAutonomous: true } };

    /// <summary>
    /// Whether the element is a form-associated custom element that is custom: the categories of form-associated elements
    /// (listed, submittable...) do not include the element while it is constructed.
    /// </summary>
    internal bool IsCustomizedFormAssociatedCustomElement => CustomElementState == CustomElementState.Custom && IsFormAssociatedCustomElement;

    internal FormAssociatedCustomElementData FormAssociatedCustomElementData => ElementRareData.FormAssociatedCustomElement ??= new FormAssociatedCustomElementData();

    /// <summary>
    /// Resets the form owner of a form-associated custom element (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#reset-the-form-owner):
    /// the form owner is computed from the tree, and <c>formAssociatedCallback</c> is enqueued when it changes.
    /// </summary>
    internal void ResetFormOwnerOfFormAssociatedCustomElement()
    {
        var data = FormAssociatedCustomElementData;
        if (IsConnected && HasAttributeWithLocalName("form"))
        {
            NodeDocument.RegisterFormAttributeCustomElement(this);
        }

        var owner = FormOwner;
        if (ReferenceEquals(owner, data.FormOwner))
            return;

        data.FormOwner = owner;
        if (CustomElementDefinition is not null)
        {
            CustomElementReactions.EnqueueCallbackReaction(this, CustomElementCallbackKind.FormAssociated, [owner]);
        }
    }

    /// <summary>Enqueues <c>formDisabledCallback</c> when the disabled state of a form-associated custom element changes.</summary>
    internal void UpdateFormAssociatedCustomElementDisabledState()
    {
        var data = FormAssociatedCustomElementData;
        var disabled = Css.Selectors.HtmlElementStates.IsFormControlDisabled(this);
        if (disabled == data.Disabled)
            return;

        data.Disabled = disabled;
        if (CustomElementDefinition is not null)
        {
            CustomElementReactions.EnqueueCallbackReaction(this, CustomElementCallbackKind.FormDisabled, [disabled]);
        }
    }

    /// <summary>The steps of the upgrade of a form-associated custom element (https://html.spec.whatwg.org/multipage/custom-elements.html#concept-upgrade-an-element, step 11).</summary>
    internal void OnFormAssociatedCustomElementUpgraded()
    {
        ResetFormOwnerOfFormAssociatedCustomElement();
        var data = FormAssociatedCustomElementData;
        data.Disabled = Css.Selectors.HtmlElementStates.IsFormControlDisabled(this);
        if (data.Disabled)
        {
            CustomElementReactions.EnqueueCallbackReaction(this, CustomElementCallbackKind.FormDisabled, [true]);
        }
    }

    /// <summary>The steps of form-associated custom elements when the element or one of its ancestors is inserted, removed or moved.</summary>
    private void OnFormAssociatedCustomElementTreeChanged()
    {
        if (!IsCustomizedFormAssociatedCustomElement)
            return;

        ResetFormOwnerOfFormAssociatedCustomElement();
        UpdateFormAssociatedCustomElementDisabledState();
    }

    /// <summary>Updates the disabled state of the form-associated custom elements of a fieldset (its disabled attribute or its first legend changed).</summary>
    private static void UpdateFormAssociatedCustomElementsDisabledState(Element fieldset)
    {
        if (!Dom.CustomElementRegistry.HasDefinitions)
            return;

        for (var node = fieldset.FirstChild; node is not null; node = node.GetNextInTreeOrder(fieldset))
        {
            if (node is HtmlElement { IsCustomizedFormAssociatedCustomElement: true } element)
            {
                element.UpdateFormAssociatedCustomElementDisabledState();
            }
        }
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        OnFormAssociatedCustomElementTreeChanged();

        // The first legend child of a fieldset is not disabled by the fieldset
        if (IsHtml(TagId.Legend) && ParentNode is Element parent && parent.IsHtml(TagId.Fieldset))
        {
            UpdateFormAssociatedCustomElementsDisabledState(parent);
        }
    }

    /// <summary>The removing steps of a legend element, whose fieldset parent may have another first legend.</summary>
    private void OnLegendRemoved(bool isSubtreeRoot, Node oldAncestor)
    {
        if (isSubtreeRoot && IsHtml(TagId.Legend) && oldAncestor is Element fieldset && fieldset.IsHtml(TagId.Fieldset))
        {
            UpdateFormAssociatedCustomElementsDisabledState(fieldset);
        }
    }

    internal override void MovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.MovingSteps(isSubtreeRoot, oldAncestor);
        OnFormAssociatedCustomElementTreeChanged();
    }

    /// <summary>The attribute change steps of custom elements (the form and disabled attributes of form-associated custom elements, the disabled attribute of fieldset elements).</summary>
    private void CustomElementAttributeChangeSteps(string localName)
    {
        if (localName == "form")
        {
            if (IsCustomizedFormAssociatedCustomElement)
            {
                ResetFormOwnerOfFormAssociatedCustomElement();
            }
        }
        else if (localName == "disabled")
        {
            if (IsCustomizedFormAssociatedCustomElement)
            {
                UpdateFormAssociatedCustomElementDisabledState();
            }
            else if (IsHtml(TagId.Fieldset))
            {
                UpdateFormAssociatedCustomElementsDisabledState(this);
            }
        }
    }

    /// <summary>The entry construction algorithm of a form-associated custom element (https://html.spec.whatwg.org/multipage/custom-elements.html#face-entry-construction).</summary>
    internal void AppendFormAssociatedCustomElementEntries(List<KeyValuePair<string, FormDataEntryValue>> entries)
    {
        var submissionValue = (RareDataOrNull as ElementRareData)?.FormAssociatedCustomElement?.SubmissionValue;
        if (submissionValue is List<KeyValuePair<string, FormDataEntryValue>> list)
        {
            entries.AddRange(list);
            return;
        }

        var name = GetAttributeValue("name");
        if (string.IsNullOrEmpty(name) || submissionValue is null)
            return;

        entries.Add(submissionValue is File file ? FormData.CreateEntry(name, file, filename: null) : FormData.CreateEntry(name, (string)submissionValue));
    }
}

public partial class Document
{
    private List<WeakReference<HtmlElement>>? _formAttributeCustomElements;

    /// <summary>Registers a form-associated custom element with a form attribute, whose form owner depends on the IDs of the tree.</summary>
    internal void RegisterFormAttributeCustomElement(HtmlElement element)
    {
        _formAttributeCustomElements ??= [];
        foreach (var reference in _formAttributeCustomElements)
        {
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, element))
                return;
        }

        _formAttributeCustomElements.Add(new WeakReference<HtmlElement>(element));
    }

    /// <summary>
    /// Resets the form owner of the form-associated custom elements with a form attribute, when an element with an ID is
    /// inserted into or removed from the document, or when the ID of an element changes.
    /// </summary>
    internal void OnIdChangedForFormAttributeCustomElements()
    {
        if (_formAttributeCustomElements is null)
            return;

        foreach (var reference in _formAttributeCustomElements.ToArray())
        {
            if (!reference.TryGetTarget(out var element))
            {
                _formAttributeCustomElements.Remove(reference);
                continue;
            }

            if (ReferenceEquals(element.NodeDocument, this) && element.IsCustomizedFormAssociatedCustomElement && element.HasAttributeWithLocalName("form"))
            {
                element.ResetFormOwnerOfFormAssociatedCustomElement();
            }
        }
    }

    internal bool HasFormAttributeCustomElements => _formAttributeCustomElements is { Count: > 0 };
}
