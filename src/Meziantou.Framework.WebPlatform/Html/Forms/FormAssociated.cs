using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The validity states of constraint validation (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#validity-states).</summary>
[Flags]
internal enum ValidityFlags
{
    None = 0,
    ValueMissing = 1 << 0,
    TypeMismatch = 1 << 1,
    PatternMismatch = 1 << 2,
    TooLong = 1 << 3,
    TooShort = 1 << 4,
    RangeUnderflow = 1 << 5,
    RangeOverflow = 1 << 6,
    StepMismatch = 1 << 7,
    BadInput = 1 << 8,
    CustomError = 1 << 9,
}

// The categories of form-associated elements and the form owner
// (https://html.spec.whatwg.org/multipage/forms.html#categories and
// https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#association-of-controls-and-forms).
public partial class HtmlElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#form-associated-element</summary>
    internal bool IsFormAssociated => TagId is TagId.Button or TagId.Fieldset or TagId.Input or TagId.Object or TagId.Output or TagId.Select or TagId.Textarea or TagId.Img || IsCustomizedFormAssociatedCustomElement;

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#category-listed</summary>
    internal bool IsListed => TagId is TagId.Button or TagId.Fieldset or TagId.Input or TagId.Object or TagId.Output or TagId.Select or TagId.Textarea || IsCustomizedFormAssociatedCustomElement;

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#category-submit</summary>
    internal bool IsSubmittable => TagId is TagId.Button or TagId.Input or TagId.Select or TagId.Textarea || IsCustomizedFormAssociatedCustomElement;

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#category-reset</summary>
    internal bool IsResettable => TagId is TagId.Input or TagId.Output or TagId.Select or TagId.Textarea || IsCustomizedFormAssociatedCustomElement;

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#category-label</summary>
    internal virtual bool IsLabelable => TagId is TagId.Button or TagId.Meter or TagId.Output or TagId.Progress or TagId.Select or TagId.Textarea || IsCustomizedFormAssociatedCustomElement;

    /// <summary>
    /// The form owner (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#form-owner). The owner is
    /// computed from the tree ("reset the form owner"), except for the elements associated with the form element pointer
    /// of the parser, which keep this form while they are in the same tree.
    /// </summary>
    internal HtmlFormElement? FormOwner
    {
        get
        {
            if (!IsFormAssociated)
                return null;

            if (RareDataOrNull is ElementRareData { ParserFormOwner: { } parserForm })
            {
                if (ReferenceEquals(parserForm.GetRoot(), GetRoot()))
                    return parserForm;

                ElementRareData.ParserFormOwner = null;
            }

            // An element with a form attribute is associated with the form element with this ID in its tree
            if (IsListed && GetAttributeValue("form") is { } formId && IsConnected)
            {
                var root = GetRoot();
                var element = root is Document document ? document.GetElementById(formId) : FindById(root, formId);
                return element as HtmlFormElement;
            }

            for (var ancestor = ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
            {
                if (ancestor is HtmlFormElement form)
                    return form;
            }

            return null;
        }
    }

    private static Element? FindById(Node root, string id)
    {
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element element && element.IdValue == id)
                return element;
        }

        return null;
    }

    /// <summary>Associates the element with the form element pointer of the parser (https://html.spec.whatwg.org/multipage/parsing.html#create-an-element-for-the-token).</summary>
    internal void AssociateWithFormFromParser(Element form)
    {
        if (form is HtmlFormElement formElement)
        {
            ElementRareData.ParserFormOwner = formElement;
        }
    }

    /// <summary>Clears the parser association ("reset the form owner" unsets the parser inserted flag).</summary>
    private protected void ResetFormOwner()
    {
        if (RareDataOrNull is ElementRareData { ParserFormOwner: not null } rareData)
        {
            rareData.ParserFormOwner = null;
        }
    }

    /// <summary>
    /// When a form-associated element or one of its ancestors is removed, its form owner is reset: as in the browsers, the
    /// association with the form element pointer of the parser is kept when the form is removed with it (they are in the same tree).
    /// </summary>
    private void OnFormAssociatedElementRemoved()
    {
        if (RareDataOrNull is ElementRareData { ParserFormOwner: { } parserForm } && !ReferenceEquals(parserForm.GetRoot(), GetRoot()))
        {
            ResetFormOwner();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels</summary>
    internal NodeList? GetLabels()
    {
        if (!IsLabelable)
            return null;

        return ElementRareData.Labels ??= new RootedNodeList(this, static (owner, root, list) =>
        {
            // The root of a disconnected tree can be a label
            for (var node = root is Element ? root : root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
            {
                if (node is HtmlLabelElement label && ReferenceEquals(label.Control, owner))
                {
                    list.Add(label);
                }
            }
        });
    }

    /// <summary>
    /// The user validity of the input, textarea and select elements (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#user-validity),
    /// used by <c>:user-valid</c> and <c>:user-invalid</c>: it becomes true when the user commits a change (or when the form
    /// is submitted), and false when the control is reset.
    /// </summary>
    internal bool UserValidity
    {
        get => RareDataOrNull is ElementRareData { UserValidity: true };
        set
        {
            if (UserValidity == value)
                return;

            ElementRareData.UserValidity = value;
            NodeDocument.IncrementElementStateVersion();
        }
    }

    /// <summary>
    /// Runs the steps of the unfocusing of a text control (https://html.spec.whatwg.org/multipage/interaction.html#focus-update-steps):
    /// when the user edited its value while it was focused, the change is committed: its user validity becomes true, and a
    /// change event is fired when the value is different from the value before the edits.
    /// </summary>
    internal void CommitUserEdits()
    {
        if (RareDataOrNull is not ElementRareData { ValueBeforeUserEdit: { } valueBeforeEdit } rareData)
            return;

        rareData.ValueBeforeUserEdit = null;
        UserValidity = true;
        var value = this switch
        {
            HtmlInputElement input => input.Value,
            HtmlTextAreaElement textArea => textArea.Value,
            _ => valueBeforeEdit,
        };

        if (value != valueBeforeEdit)
        {
            FireEvent(new Event("change", new EventInit { Bubbles = true }));
        }
    }

    // Constraint validation (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#constraints)

    /// <summary>Whether the element is barred from constraint validation (in addition to the common conditions).</summary>
    private protected virtual bool IsBarredFromConstraintValidation => !IsSubmittable || (IsCustomizedFormAssociatedCustomElement && HasAttributeWithLocalName("readonly"));

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#candidate-for-constraint-validation</summary>
    internal bool IsCandidateForConstraintValidation
    {
        get
        {
            if (IsBarredFromConstraintValidation)
                return false;

            // Disabled elements and elements with a datalist ancestor are barred
            if (Css.Selectors.HtmlElementStates.IsActuallyDisabled(this))
                return false;

            for (var ancestor = ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
            {
                if (ancestor.IsHtml(TagId.Datalist))
                    return false;
            }

            return true;
        }
    }

    /// <summary>The validity states of the element, other than the custom error.</summary>
    private protected virtual ValidityFlags ComputeValidity()
    {
        // The custom error of a form-associated custom element is its custom validity error message
        if (RareDataOrNull is ElementRareData { FormAssociatedCustomElement: { } data } && IsFormAssociatedCustomElement)
            return data.ValidityFlags & ~ValidityFlags.CustomError;

        return ValidityFlags.None;
    }

    internal ValidityFlags GetValidityFlags()
    {
        var flags = ComputeValidity();
        if (!string.IsNullOrEmpty(CustomValidityErrorMessage))
        {
            flags |= ValidityFlags.CustomError;
        }

        return flags;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#custom-validity-error-message</summary>
    internal string CustomValidityErrorMessage
    {
        get => (RareDataOrNull as ElementRareData)?.CustomValidityErrorMessage ?? "";
        set
        {
            var wasEmpty = CustomValidityErrorMessage.Length == 0;
            ElementRareData.CustomValidityErrorMessage = value;

            // The validity of the element (and of its form and fieldset ancestors) matched by :valid and :invalid changes
            if (wasEmpty != (value.Length == 0))
            {
                NodeDocument.IncrementElementStateVersion();
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-fv-valid</summary>
    internal bool SatisfiesConstraints => GetValidityFlags() == ValidityFlags.None;

    internal ValidityState GetValidity() => ElementRareData.Validity ??= new ValidityState(this);

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage</summary>
    internal string GetValidationMessage()
    {
        if (!IsCandidateForConstraintValidation)
            return "";

        var flags = GetValidityFlags();
        if (flags == ValidityFlags.None)
            return "";

        if ((flags & ValidityFlags.CustomError) != ValidityFlags.None)
            return CustomValidityErrorMessage;

        return flags switch
        {
            _ when (flags & ValidityFlags.ValueMissing) != ValidityFlags.None => "Please fill out this field.",
            _ when (flags & ValidityFlags.TypeMismatch) != ValidityFlags.None => "Please enter a valid value.",
            _ when (flags & ValidityFlags.PatternMismatch) != ValidityFlags.None => "Please match the requested format.",
            _ when (flags & ValidityFlags.TooLong) != ValidityFlags.None => "Please shorten this text.",
            _ when (flags & ValidityFlags.TooShort) != ValidityFlags.None => "Please lengthen this text.",
            _ when (flags & ValidityFlags.RangeUnderflow) != ValidityFlags.None => "The value is too low.",
            _ when (flags & ValidityFlags.RangeOverflow) != ValidityFlags.None => "The value is too high.",
            _ when (flags & ValidityFlags.StepMismatch) != ValidityFlags.None => "Please enter a valid value.",
            _ => "Please enter a valid value.",
        };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#check-validity-steps</summary>
    internal bool CheckValidityCore()
    {
        if (!IsCandidateForConstraintValidation || SatisfiesConstraints)
            return true;

        FireEvent(new Event("invalid", new EventInit { Cancelable = true }));
        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity</summary>
    internal void SetCustomValidityCore(string error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // Normalize newlines
        CustomValidityErrorMessage = error.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#validitystate</summary>
[IdlInterface("ValidityState", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#validitystate")]
public sealed class ValidityState
{
    private readonly HtmlElement _element;

    internal ValidityState(HtmlElement element)
    {
        _element = element;
    }

    // The validity states do not depend on whether the element is a candidate for constraint validation
    private bool Has(ValidityFlags flag) => (_element.GetValidityFlags() & flag) != ValidityFlags.None;

    /// <summary>Gets a value indicating whether the control is required but has no value.</summary>
    [IdlMember("valueMissing", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-valuemissing")]
    public bool ValueMissing => Has(ValidityFlags.ValueMissing);

    /// <summary>
    /// Gets a value indicating whether the value is not in the required syntax (such as an e-mail address or a URL).
    /// </summary>
    [IdlMember("typeMismatch", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-typemismatch")]
    public bool TypeMismatch => Has(ValidityFlags.TypeMismatch);

    /// <summary>Gets a value indicating whether the value does not match the required pattern.</summary>
    [IdlMember("patternMismatch", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-patternmismatch")]
    public bool PatternMismatch => Has(ValidityFlags.PatternMismatch);

    /// <summary>Gets a value indicating whether the value is longer than the maximum length.</summary>
    [IdlMember("tooLong", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-toolong")]
    public bool TooLong => Has(ValidityFlags.TooLong);

    /// <summary>Gets a value indicating whether the value is shorter than the minimum length.</summary>
    [IdlMember("tooShort", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-tooshort")]
    public bool TooShort => Has(ValidityFlags.TooShort);

    /// <summary>Gets a value indicating whether the value is lower than the minimum.</summary>
    [IdlMember("rangeUnderflow", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-rangeunderflow")]
    public bool RangeUnderflow => Has(ValidityFlags.RangeUnderflow);

    /// <summary>Gets a value indicating whether the value is greater than the maximum.</summary>
    [IdlMember("rangeOverflow", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-rangeoverflow")]
    public bool RangeOverflow => Has(ValidityFlags.RangeOverflow);

    /// <summary>Gets a value indicating whether the value does not fit the allowed step.</summary>
    [IdlMember("stepMismatch", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-stepmismatch")]
    public bool StepMismatch => Has(ValidityFlags.StepMismatch);

    /// <summary>Gets a value indicating whether the user provided input that cannot be converted to a value.</summary>
    [IdlMember("badInput", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-badinput")]
    public bool BadInput => Has(ValidityFlags.BadInput);

    /// <summary>Gets a value indicating whether a custom validity error message is set.</summary>
    [IdlMember("customError", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-customerror")]
    public bool CustomError => Has(ValidityFlags.CustomError);

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-valid</summary>
    [IdlMember("valid", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-validitystate-valid")]
    public bool Valid => _element.SatisfiesConstraints;
}

/// <summary>A live list of nodes computed from the root of the owner (e.g. the labels of a control).</summary>
internal sealed class RootedNodeList(Node owner, Action<Node, Node, List<Node>> populate) : NodeList
{
    private readonly List<Node> _nodes = [];
    private int _version = -1;
    private Node? _root;

    private List<Node> Nodes
    {
        get
        {
            var root = owner.GetRoot();
            var version = owner.NodeDocument.DomVersion;
            if (version != _version || !ReferenceEquals(root, _root))
            {
                _nodes.Clear();
                populate(owner, root, _nodes);
                _version = version;
                _root = root;
            }

            return _nodes;
        }
    }

    public override uint Length => (uint)Nodes.Count;

    public override Node? Item(uint index)
    {
        var nodes = Nodes;
        return index < (uint)nodes.Count ? nodes[(int)index] : null;
    }
}
