using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#the-textarea-element</summary>
public partial class HtmlTextAreaElement : IEditableTextControl
{
    // The raw value; null when the dirty value flag is false (the raw value is then the child text content)
    private string? _rawValue;
    private int _selectionStart;
    private int _selectionEnd;
    private string _selectionDirection = "none";

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-textarea-raw-value</summary>
    private string RawValue => _rawValue ?? GetChildTextContent();

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-textarea-api-value</summary>
    private string ApiValue => NormalizeNewlines(RawValue);

    private static string NormalizeNewlines(string value) => value.Contains('\r', StringComparison.Ordinal) ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : value;

    /// <summary>Gets the type of the control, always <c>textarea</c>.</summary>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-textarea-type")]
    public string Type => "textarea";

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-textarea-defaultvalue</summary>
    [IdlMember("defaultValue", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-textarea-defaultvalue")]
    public string DefaultValue
    {
        get => GetChildTextContent();
        set => StringReplaceAll(value ?? "");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-textarea-value</summary>
    [IdlMember("value", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-textarea-value")]
    public string Value
    {
        get => ApiValue;
        set
        {
            var oldApiValue = ApiValue;
            _rawValue = value ?? "";
            NodeDocument.IncrementElementStateVersion();
            TextControlEditing.ClearHistory(this);
            if (ApiValue != oldApiValue)
            {
                _selectionStart = _selectionEnd = ApiValue.Length;
                _selectionDirection = "none";
            }
        }
    }

    /// <summary>Gets the length of the value of the control, in UTF-16 code units.</summary>
    [IdlMember("textLength", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-textarea-textlength")]
    public uint TextLength => (uint)ApiValue.Length;

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#the-textarea-element:concept-form-reset-control</summary>
    internal override void ResetAlgorithm()
    {
        UserValidity = false;
        _rawValue = null;
        TextControlEditing.ClearHistory(this);
        NodeDocument.IncrementElementStateVersion();
        _selectionStart = _selectionEnd = 0;
        _selectionDirection = "none";
    }

    internal override void ChildrenChangedSteps(bool childrenInserted)
    {
        base.ChildrenChangedSteps(childrenInserted);
        ClampSelectionToChildTextContent();
    }

    internal override void ChildTextContentChanged()
    {
        base.ChildTextContentChanged();
        ClampSelectionToChildTextContent();
    }

    /// <summary>The selection stays within the bounds of the raw value when the child text content of a textarea whose dirty value flag is false changes.</summary>
    private void ClampSelectionToChildTextContent()
    {
        if (_rawValue is not null)
            return;

        var length = ApiValue.Length;
        _selectionStart = Math.Min(_selectionStart, length);
        _selectionEnd = Math.Min(_selectionEnd, length);
    }

    internal override void CloningSteps(Node copy, bool subtree)
    {
        base.CloningSteps(copy, subtree);
        ((HtmlTextAreaElement)copy)._rawValue = _rawValue;
    }

    internal bool IsMutable => !Css.Selectors.HtmlElementStates.IsActuallyDisabled(this) && !HasAttributeWithLocalName("readonly");

    private protected override bool IsBarredFromConstraintValidation => HasAttributeWithLocalName("readonly");

    private protected override ValidityFlags ComputeValidity()
    {
        // tooLong and tooShort only apply to values edited by the user
        if (HasAttributeWithLocalName("required") && IsMutable && ApiValue.Length == 0)
            return ValidityFlags.ValueMissing;

        return ValidityFlags.None;
    }

    bool IEditableTextControl.IsTextEditor => true;

    bool IEditableTextControl.IsEditMutable => IsMutable;

    bool IEditableTextControl.IsMultiline => true;

    bool IEditableTextControl.IsPassword => false;

    string IEditableTextControl.EditValue => ApiValue;

    int IEditableTextControl.EditSelectionStart => Math.Min(_selectionStart, ApiValue.Length);

    int IEditableTextControl.EditSelectionEnd => Math.Min(_selectionEnd, ApiValue.Length);

    string IEditableTextControl.EditSelectionDirection => _selectionDirection;

    int? IEditableTextControl.EditMaxLength => GetAttributeValue("maxlength") is { } maxLength && Microsyntaxes.TryParseNonNegativeInteger(maxLength, out var value) ? (int)Math.Min(value, int.MaxValue) : null;

    string IEditableTextControl.ApplyUserEdit(string value, int selectionStart, int selectionEnd, string direction)
    {
        _rawValue = value;
        NodeDocument.IncrementElementStateVersion();
        var length = ApiValue.Length;
        TextControlSelection.SetUserSelection(Selection, Math.Min(selectionStart, length), Math.Min(selectionEnd, length), direction);
        return ApiValue;
    }

    void IEditableTextControl.SetUserSelection(int selectionStart, int selectionEnd, string direction) => TextControlSelection.SetUserSelection(Selection, selectionStart, selectionEnd, direction);

    private TextSelection Selection => new(this);

    /// <summary>Selects all the text of the control.</summary>
    [IdlMember("select", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-select")]
    public void Select() => Selection.SetRange(0, ApiValue.Length, "none");

    /// <summary>Gets or sets the index of the start of the text selection.</summary>
    [IdlMember("selectionStart", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectionstart")]
    public uint SelectionStart
    {
        get => (uint)Math.Min(_selectionStart, ApiValue.Length);
        set => TextControlSelection.SetSelectionRange(Selection, value, Math.Max(value, SelectionEnd), _selectionDirection);
    }

    /// <summary>Gets or sets the index of the end of the text selection.</summary>
    [IdlMember("selectionEnd", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectionend")]
    public uint SelectionEnd
    {
        get => (uint)Math.Min(_selectionEnd, ApiValue.Length);
        set => TextControlSelection.SetSelectionRange(Selection, SelectionStart, value, _selectionDirection);
    }

    /// <summary>
    /// Gets or sets the direction of the text selection (<c>forward</c>, <c>backward</c> or <c>none</c>).
    /// </summary>
    [IdlMember("selectionDirection", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectiondirection")]
    public string SelectionDirection
    {
        get => _selectionDirection;
        set => TextControlSelection.SetSelectionRange(Selection, SelectionStart, SelectionEnd, value);
    }

    /// <summary>Replaces the selected text with the given text.</summary>
    [IdlMember("setRangeText", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setrangetext")]
    public void SetRangeText(string replacement) => TextControlSelection.SetRangeText(Selection, replacement, null, null, "preserve");

    /// <summary>
    /// Replaces the text between the start and end indexes with the given text and updates the selection according to
    /// the selection mode.
    /// </summary>
    [IdlMember("setRangeText", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setrangetext")]
    public void SetRangeText(string replacement, uint start, uint end, string selectionMode = "preserve") => TextControlSelection.SetRangeText(Selection, replacement, start, end, selectionMode);

    /// <summary>Selects the text between the start and end indexes, with the given direction.</summary>
    [IdlMember("setSelectionRange", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setselectionrange")]
    public void SetSelectionRange(uint start, uint end, string? direction = null) => TextControlSelection.SetSelectionRange(Selection, start, end, direction);

    private readonly struct TextSelection(HtmlTextAreaElement textArea) : ITextControl
    {
        public string Text
        {
            get => textArea.ApiValue;
            set
            {
                textArea._rawValue = value;
                textArea.NodeDocument.IncrementElementStateVersion();
                TextControlEditing.ClearHistory(textArea);
            }
        }

        public HtmlElement Element => textArea;

        public int Start { get => Math.Min(textArea._selectionStart, Text.Length); set => textArea._selectionStart = value; }

        public int End { get => Math.Min(textArea._selectionEnd, Text.Length); set => textArea._selectionEnd = value; }

        public string Direction { get => textArea._selectionDirection; set => textArea._selectionDirection = value; }

        public void SetRange(long start, long end, string? direction) => TextControlSelection.SetSelectionRange(this, start, end, direction);
    }
}
