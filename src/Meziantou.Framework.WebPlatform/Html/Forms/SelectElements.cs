using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmloptionscollection</summary>
[IdlInterface("HTMLOptionsCollection", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmloptionscollection")]
public sealed class HtmlOptionsCollection : HtmlCollection
{
    private readonly HtmlSelectElement _select;

    internal HtmlOptionsCollection(HtmlSelectElement select)
        : base(select, list => list.AddRange(select.GetListOfOptions()))
    {
        _select = select;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-length</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-length")]
    public new uint Length
    {
        get => base.Length;
        set
        {
            var current = base.Length;
            if (value > current)
            {
                if (value > 100_000)
                    return;

                _select.AppendNewOptionElements(value - current);
            }
            else
            {
                _select.UpdateOptionsInBatch(value, static (select, length) =>
                {
                    var options = select.GetListOfOptions();
                    for (var i = options.Count - 1; i >= length; i--)
                    {
                        options[i].RemoveFromParent();
                    }
                });
            }
        }
    }

    /// <summary>The indexed setter (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-setter).</summary>
    [IdlMember("setter", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-setter")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public new HtmlOptionElement? this[uint index]
    {
        get => base[index] as HtmlOptionElement;
        set => SetItem(index, value);
    }

    internal void SetItem(uint index, HtmlOptionElement? option)
    {
        if (option is null)
        {
            Remove((int)Math.Min(index, int.MaxValue));
            return;
        }

        var length = base.Length;
        if (index >= length)
        {
            if (index > length)
            {
                _select.AppendNewOptionElements(index - length);
            }

            _select.AppendChild(option);
            return;
        }

        var existing = base[index]!;
        existing.ParentNode!.ReplaceChild(option, existing);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-add</summary>
    [IdlMember("add", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-add")]
    public void Add(HtmlElement element, HtmlElement? before = null) => AddCore(element, before, null);

    /// <summary>
    /// Inserts an <c>option</c> or <c>optgroup</c> element before the option at the given index, or at the end when the
    /// index is invalid.
    /// </summary>
    [IdlMember("add", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-add")]
    public void Add(HtmlElement element, int before) => AddCore(element, null, before);

    internal void AddCore(HtmlElement element, HtmlElement? beforeElement, int? beforeIndex)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element is not (HtmlOptionElement or HtmlOptGroupElement))
            throw new TypeErrorException("The element must be an option or an optgroup element");

        if (element.IsInclusiveAncestorOf(_select))
            throw DomException.HierarchyRequest("The element is an ancestor of the select element");

        if (beforeElement is not null && !_select.IsAncestorOf(beforeElement))
            throw DomException.NotFound("The before element is not a descendant of the select element");

        if (ReferenceEquals(element, beforeElement))
            return;

        Node? reference = beforeElement;
        if (beforeIndex is { } index)
        {
            reference = index >= 0 ? base[(uint)index] : null;
        }

        var parent = reference?.ParentNode ?? _select;
        parent.InsertBefore(element, reference);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-remove</summary>
    [IdlMember("remove", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-remove")]
    public void Remove(int index)
    {
        if (Count == 0 || index < 0 || index >= Count)
            return;

        base[(uint)index]!.RemoveFromParent();
    }

    /// <summary>Gets or sets the index of the first selected option, or -1 when no option is selected.</summary>
    [IdlMember("selectedIndex", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmloptionscollection-selectedindex")]
    public int SelectedIndex
    {
        get => _select.SelectedIndex;
        set => _select.SelectedIndex = value;
    }
}

public partial class HtmlSelectElement
{
    private HtmlOptionsCollection? _options;
    private HtmlCollection? _selectedOptions;

    internal HtmlOptionElement CreateOption() => (HtmlOptionElement)ElementFactory.CreateElement(NodeDocument, "option", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#append-new-option-elements</summary>
    internal void AppendNewOptionElements(uint count)
    {
        var fragment = new DocumentFragment(NodeDocument);
        for (var i = 0u; i < count; i++)
        {
            fragment.AppendChildCore(CreateOption());
        }

        UpdateOptionsInBatch(fragment, static (select, fragment) => select.AppendChild(fragment));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-type</summary>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-type")]
    public string Type => IsMultiple ? "select-multiple" : "select-one";

    /// <summary>Gets the collection of the options of the select element.</summary>
    [IdlMember("options", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-options")]
    public HtmlOptionsCollection Options => _options ??= new HtmlOptionsCollection(this);

    /// <summary>Gets or sets the number of options; setting it adds blank options or removes the last ones.</summary>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-length")]
    public uint Length
    {
        get => Options.Length;
        set => Options.Length = value;
    }

    /// <summary>Returns the option at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-item")]
    public HtmlOptionElement? Item(uint index) => Options[index];

    /// <summary>Gets or sets the option at the given index; setting null removes the option.</summary>
    [IdlMember("setter", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-setter")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public HtmlOptionElement? this[uint index]
    {
        get => Options[index];
        set => Options.SetItem(index, value);
    }

    /// <summary>
    /// Returns the first option whose <c>id</c> or <c>name</c> matches the given name, or null when none matches.
    /// </summary>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-nameditem")]
    public HtmlOptionElement? NamedItem(string name) => Options.NamedItem(name) as HtmlOptionElement;

    /// <summary>
    /// Inserts an <c>option</c> or <c>optgroup</c> element before the given element, or at the end when no element is
    /// given.
    /// </summary>
    [IdlMember("add", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-add")]
    public void Add(HtmlElement element, HtmlElement? before = null) => Options.AddCore(element, before, null);

    /// <summary>
    /// Inserts an <c>option</c> or <c>optgroup</c> element before the option at the given index, or at the end when the
    /// index is invalid.
    /// </summary>
    [IdlMember("add", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-add")]
    public void Add(HtmlElement element, int before) => Options.AddCore(element, null, before);

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-remove</summary>
    [IdlMember("remove", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-remove")]
    public void Remove(int index) => Options.Remove(index);

    /// <summary>Gets the collection of the options that are currently selected.</summary>
    [IdlMember("selectedOptions", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-selectedoptions")]
    public HtmlCollection SelectedOptions => _selectedOptions ??= new HtmlCollection(this, list => list.AddRange(GetListOfOptions().Where(o => o.Selectedness)));

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-selectedindex</summary>
    [IdlMember("selectedIndex", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-selectedindex")]
    public int SelectedIndex
    {
        get
        {
            var options = GetListOfOptions();
            return options.FindIndex(o => o.Selectedness);
        }
        set
        {
            var options = GetListOfOptions();
            foreach (var option in options)
            {
                option.Selectedness = false;
            }

            if (value >= 0 && value < options.Count)
            {
                options[value].Selectedness = true;
                options[value].Dirtiness = true;
            }

            UpdateDescendantSelectedContentElements();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-value</summary>
    [IdlMember("value", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-select-value")]
    public string Value
    {
        get => GetListOfOptions().Find(o => o.Selectedness)?.Value ?? "";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var found = false;
            foreach (var option in GetListOfOptions())
            {
                if (!found && option.Value == value)
                {
                    option.Selectedness = true;
                    option.Dirtiness = true;
                    found = true;
                }
                else
                {
                    option.Selectedness = false;
                }
            }

            UpdateDescendantSelectedContentElements();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-select-showpicker</summary>
    [IdlMember("showPicker", "https://html.spec.whatwg.org/multipage/input.html#dom-select-showpicker")]
    public void ShowPicker()
    {
        if (Css.Selectors.HtmlElementStates.IsActuallyDisabled(this))
            throw DomException.InvalidState("The element is not mutable");

        if (!OriginRecord.IsSameOrigin(NodeDocument.Origin, NodeDocument.TopLevelOrigin))
            throw new DomException("showPicker() cannot be called in a cross-origin iframe", "SecurityError");

        var window = NodeDocument.RelevantWindow;
        if (window is not { HasTransientActivation: true })
            throw new DomException("showPicker() requires a user activation", "NotAllowedError");

        // "If this is not being rendered, then throw a "NotSupportedError" DOMException."
        if (!FocusModel.IsBeingRendered(this))
            throw DomException.NotSupported("The element is not being rendered");

        // "Show the picker, if applicable": it consumes the user activation
        window.ConsumeUserActivation();
        OpenPicker(focusVisible: null);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element:concept-form-reset-control</summary>
    internal override void ResetAlgorithm()
    {
        UserValidity = false;
        ResetFromParser();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#placeholder-label-option</summary>
    private HtmlOptionElement? GetPlaceholderLabelOption()
    {
        if (HasAttributeWithLocalName("multiple") || DisplaySize != 1)
            return null;

        // "the value of the first option element in the select element's list of options (if any) is the empty string, and that
        // option element does not have an ancestor optgroup in between itself and the select element"
        var first = GetListOfOptions().FirstOrDefault();
        if (first is null || first.Value.Length != 0)
            return null;

        for (var ancestor = first.ParentNode; ancestor is not null && !ReferenceEquals(ancestor, this); ancestor = ancestor.ParentNode)
        {
            if (ancestor is HtmlOptGroupElement)
                return null;
        }

        return first;
    }

    private protected override ValidityFlags ComputeValidity()
    {
        if (!HasAttributeWithLocalName("required"))
            return ValidityFlags.None;

        // The element is suffering from being missing when no option is selected, or only the placeholder label option
        var selected = GetListOfOptions().Where(o => o.Selectedness).ToList();
        var placeholder = GetPlaceholderLabelOption();
        if (selected.Count == 0 || (placeholder is not null && selected.Count == 1 && ReferenceEquals(selected[0], placeholder)))
            return ValidityFlags.ValueMissing;

        return ValidityFlags.None;
    }
}

public partial class HtmlOptionElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-selected</summary>
    [IdlMember("selected", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-selected")]
    public bool Selected
    {
        get => Selectedness;
        set
        {
            Selectedness = value;
            Dirtiness = true;
            if (value && CachedNearestAncestorSelect is { IsMultiple: false } select)
            {
                foreach (var option in select.GetListOfOptions())
                {
                    if (!ReferenceEquals(option, this))
                    {
                        option.Selectedness = false;
                    }
                }
            }

            // Ask for a reset
            if (CachedNearestAncestorSelect is { } owner)
            {
                owner.RunSelectednessSettingAlgorithm();
                owner.UpdateDescendantSelectedContentElements();
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-text</summary>
    [IdlMember("text", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-text")]
    public string Text
    {
        get => Reflection.GetHtmlAwareTextContent(this, includeAltText: false);
        set => StringReplaceAll(value ?? "");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-option-index</summary>
    [IdlMember("index", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-index")]
    public int Index
    {
        get
        {
            if (CachedNearestAncestorSelect is not { } select)
                return 0;

            var index = select.GetListOfOptions().IndexOf(this);
            return index < 0 ? 0 : index;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-form</summary>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-option-form")]
    public HtmlFormElement? Form => CachedNearestAncestorSelect?.FormOwner;

    /// <summary>
    /// The Option() legacy factory function (https://html.spec.whatwg.org/multipage/form-elements.html#dom-option).
    /// </summary>
    [IdlMember("Option", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-option")]
    public static HtmlOptionElement Create(Document document, string text = "", string? value = null, bool defaultSelected = false, bool selected = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var option = (HtmlOptionElement)ElementFactory.CreateElement(document, "option", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        if (!string.IsNullOrEmpty(text))
        {
            option.AppendChild(document.CreateTextNode(text));
        }

        if (value is not null)
        {
            option.SetAttributeValue("value", value);
        }

        if (defaultSelected)
        {
            option.SetAttributeValue("selected", "");
        }

        option.Selectedness = selected;
        return option;
    }
}

public partial class HtmlDataListElement
{
    private HtmlCollection? _options;

    /// <summary>
    /// Gets the collection of the <c>option</c> elements that are descendants of the <c>datalist</c> element.
    /// </summary>
    [IdlMember("options", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-datalist-options")]
    public HtmlCollection Options => _options ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Option));
}
