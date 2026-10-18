using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#elementinternals</summary>
[IdlInterface("ElementInternals", "https://html.spec.whatwg.org/multipage/custom-elements.html#elementinternals")]
public sealed partial class ElementInternals
{
    private Dictionary<string, WeakReference<Element>>? _explicitlySetAttrElements;
    private Dictionary<string, List<WeakReference<Element>>>? _explicitlySetAttrElementLists;
    private Dictionary<string, Element[]>? _cachedAttrAssociatedElements;

    internal ElementInternals(HtmlElement target)
    {
        Target = target;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#internals-target</summary>
    internal HtmlElement Target { get; }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-shadowroot</summary>
    [IdlMember("shadowRoot", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-shadowroot")]
    public ShadowRoot? ShadowRoot => Target.AttachedShadowRoot is { AvailableToElementInternals: true } shadow ? shadow : null;

    private FormAssociatedCustomElementData GetFormAssociatedData()
    {
        if (!Target.IsFormAssociatedCustomElement)
            throw DomException.NotSupported("The target element is not a form-associated custom element");

        return Target.FormAssociatedCustomElementData;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-setformvalue</summary>
    [IdlMember("setFormValue", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-setformvalue")]
    public void SetFormValue(FileOrStringOrFormData? value)
    {
        var data = GetFormAssociatedData();
        data.SubmissionValue = ToFormValue(value);
        data.State = data.SubmissionValue;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-setformvalue</summary>
    [IdlMember("setFormValue", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-setformvalue")]
    public void SetFormValue(FileOrStringOrFormData? value, FileOrStringOrFormData? state)
    {
        var data = GetFormAssociatedData();
        data.SubmissionValue = ToFormValue(value);
        data.State = ToFormValue(state);
    }

    /// <summary>A submission value or a state: null, a string, a File, or a copy of the entry list of a FormData.</summary>
    private static object? ToFormValue(FileOrStringOrFormData? value) => value?.Value switch
    {
        null => null,
        FormData formData => formData.Entries.ToList(),
        var other => other,
    };

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-form</summary>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/forms.html#dom-elementinternals-form")]
    public HtmlFormElement? Form
    {
        get
        {
            GetFormAssociatedData();
            return Target.FormOwner;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-setvalidity</summary>
    [IdlMember("setValidity", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-setvalidity")]
    public void SetValidity(ValidityStateFlags? flags = null, string? message = null, HtmlElement? anchor = null)
    {
        var data = GetFormAssociatedData();
        flags ??= new ValidityStateFlags();
        message ??= "";
        var validityFlags = flags.ToValidityFlags();
        if (validityFlags != ValidityFlags.None && message.Length == 0)
            throw new TypeErrorException("A message is required when a validity flag is set");

        message = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        data.ValidityFlags = validityFlags;
        data.ValidationMessage = validityFlags == ValidityFlags.None ? "" : message;
        Target.CustomValidityErrorMessage = (validityFlags & ValidityFlags.CustomError) != ValidityFlags.None ? data.ValidationMessage : "";
        if (anchor is null)
        {
            anchor = Target;
        }
        else if (!Target.IsShadowIncludingInclusiveAncestorOf(anchor))
        {
            throw DomException.NotFound("The anchor must be a shadow-including inclusive descendant of the element");
        }

        data.ValidationAnchor = anchor;
        Target.NodeDocument.IncrementElementStateVersion();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-willvalidate</summary>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-elementinternals-willvalidate")]
    public bool WillValidate
    {
        get
        {
            GetFormAssociatedData();
            return Target.IsCandidateForConstraintValidation;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-validity</summary>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-elementinternals-validity")]
    public ValidityState Validity
    {
        get
        {
            GetFormAssociatedData();
            return Target.GetValidity();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-validationmessage</summary>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-elementinternals-validationmessage")]
    public string ValidationMessage => GetFormAssociatedData().ValidationMessage;

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-elementinternals-checkvalidity</summary>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-elementinternals-checkvalidity")]
    public bool CheckValidity()
    {
        GetFormAssociatedData();
        return Target.CheckValidityCore();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-elementinternals-reportvalidity</summary>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-elementinternals-reportvalidity")]
    public bool ReportValidity()
    {
        GetFormAssociatedData();

        // There is no user interface: the problems are not reported to the user
        return Target.CheckValidityCore();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#dom-elementinternals-labels</summary>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-elementinternals-labels")]
    public NodeList Labels
    {
        get
        {
            GetFormAssociatedData();
            return Target.GetLabels()!;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-states</summary>
    [IdlMember("states", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-states")]
    public CustomStateSet States => Target.States;

    // The ARIAMixin members reflect the internal content attribute map of the target element
    // (https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#reflected-target)

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#internal-content-attribute-map</summary>
    private Dictionary<string, string> InternalContentAttributeMap => Target.InternalContentAttributeMap;

    private string? GetAriaString(string name) => Target.InternalContentAttributeMapOrNull?.GetValueOrDefault(name);

    private void SetAriaString(string name, string? value)
    {
        if (value is null)
        {
            Target.InternalContentAttributeMapOrNull?.Remove(name);
        }
        else
        {
            InternalContentAttributeMap[name] = value;
        }

        // Changing the content attribute clears the explicitly set elements
        _explicitlySetAttrElements?.Remove(name);
        _explicitlySetAttrElementLists?.Remove(name);
    }

    private Element? GetAriaElement(string name)
    {
        if (_explicitlySetAttrElements?.TryGetValue(name, out var reference) == true)
            return reference.TryGetTarget(out var explicitlySet) && IsDescendantOfShadowIncludingAncestor(explicitlySet) ? explicitlySet : null;

        var value = GetAriaString(name);
        return value is null ? null : FindById(value);
    }

    private void SetAriaElement(string name, Element? value)
    {
        SetAriaString(name, value is null ? null : "");
        if (value is not null)
        {
            (_explicitlySetAttrElements ??= new(StringComparer.Ordinal))[name] = new WeakReference<Element>(value);
        }
    }

    private Element[]? GetAriaElements(string name)
    {
        List<Element>? elements = null;
        if (_explicitlySetAttrElementLists?.TryGetValue(name, out var references) == true)
        {
            elements = [];
            foreach (var reference in references)
            {
                if (reference.TryGetTarget(out var target) && IsDescendantOfShadowIncludingAncestor(target))
                {
                    elements.Add(target);
                }
            }
        }
        else if (GetAriaString(name) is { } value)
        {
            elements = [];
            foreach (var id in InfraString.SplitOnAsciiWhitespace(value))
            {
                if (FindById(id) is { } element)
                {
                    elements.Add(element);
                }
            }
        }

        if (elements is null)
        {
            _cachedAttrAssociatedElements?.Remove(name);
            return null;
        }

        // Return the same object while the elements are the same
        var cache = _cachedAttrAssociatedElements ??= new(StringComparer.Ordinal);
        if (cache.TryGetValue(name, out var cached) && cached.AsSpan().SequenceEqual(elements.ToArray()))
            return cached;

        var result = elements.ToArray();
        cache[name] = result;
        return result;
    }

    private void SetAriaElements(string name, IReadOnlyList<Element>? value)
    {
        SetAriaString(name, value is null ? null : "");
        if (value is not null)
        {
            var references = new List<WeakReference<Element>>(value.Count);
            foreach (var item in value)
            {
                references.Add(new WeakReference<Element>(item));
            }

            (_explicitlySetAttrElementLists ??= new(StringComparer.Ordinal))[name] = references;
        }
    }

    private Element? FindById(string id)
    {
        var root = Target.GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element candidate && candidate.IdValue == id)
                return candidate;
        }

        return null;
    }

    private bool IsDescendantOfShadowIncludingAncestor(Element element)
    {
        for (Node? ancestor = Target; ancestor is not null; ancestor = ancestor.ParentNode is { } parent ? parent : (ancestor as ShadowRoot)?.Host)
        {
            if (ancestor.IsAncestorOf(element))
                return true;
        }

        return false;
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#validitystateflags</summary>
[IdlInterface("ValidityStateFlags", "https://html.spec.whatwg.org/multipage/custom-elements.html#validitystateflags")]
public sealed class ValidityStateFlags
{
    /// <summary>Gets a value indicating whether the control is required but has no value.</summary>
    [IdlMember("valueMissing", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-valuemissing")]
    public bool ValueMissing { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value is not in the required syntax (such as an e-mail address or a URL).
    /// </summary>
    [IdlMember("typeMismatch", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-typemismatch")]
    public bool TypeMismatch { get; init; }

    /// <summary>Gets a value indicating whether the value does not match the required pattern.</summary>
    [IdlMember("patternMismatch", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-patternmismatch")]
    public bool PatternMismatch { get; init; }

    /// <summary>Gets a value indicating whether the value is longer than the maximum length.</summary>
    [IdlMember("tooLong", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-toolong")]
    public bool TooLong { get; init; }

    /// <summary>Gets a value indicating whether the value is shorter than the minimum length.</summary>
    [IdlMember("tooShort", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-tooshort")]
    public bool TooShort { get; init; }

    /// <summary>Gets a value indicating whether the value is lower than the minimum.</summary>
    [IdlMember("rangeUnderflow", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-rangeunderflow")]
    public bool RangeUnderflow { get; init; }

    /// <summary>Gets a value indicating whether the value is greater than the maximum.</summary>
    [IdlMember("rangeOverflow", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-rangeoverflow")]
    public bool RangeOverflow { get; init; }

    /// <summary>Gets a value indicating whether the value does not fit the allowed step.</summary>
    [IdlMember("stepMismatch", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-stepmismatch")]
    public bool StepMismatch { get; init; }

    /// <summary>Gets a value indicating whether the user provided input that cannot be converted to a value.</summary>
    [IdlMember("badInput", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-badinput")]
    public bool BadInput { get; init; }

    /// <summary>Gets a value indicating whether a custom validity error message is set.</summary>
    [IdlMember("customError", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-validitystateflags-customerror")]
    public bool CustomError { get; init; }

    internal ValidityFlags ToValidityFlags()
    {
        var result = ValidityFlags.None;
        if (ValueMissing)
        {
            result |= ValidityFlags.ValueMissing;
        }

        if (TypeMismatch)
        {
            result |= ValidityFlags.TypeMismatch;
        }

        if (PatternMismatch)
        {
            result |= ValidityFlags.PatternMismatch;
        }

        if (TooLong)
        {
            result |= ValidityFlags.TooLong;
        }

        if (TooShort)
        {
            result |= ValidityFlags.TooShort;
        }

        if (RangeUnderflow)
        {
            result |= ValidityFlags.RangeUnderflow;
        }

        if (RangeOverflow)
        {
            result |= ValidityFlags.RangeOverflow;
        }

        if (StepMismatch)
        {
            result |= ValidityFlags.StepMismatch;
        }

        if (BadInput)
        {
            result |= ValidityFlags.BadInput;
        }

        if (CustomError)
        {
            result |= ValidityFlags.CustomError;
        }

        return result;
    }
}

/// <summary>The <c>(File or USVString or FormData)</c> union of <see cref="ElementInternals.SetFormValue(FileOrStringOrFormData?)"/>.</summary>
[IdlInterface("(File or USVString or FormData)", "https://html.spec.whatwg.org/multipage/custom-elements.html#dom-elementinternals-setformvalue")]
public readonly struct FileOrStringOrFormData : IUnionValue, IEquatable<FileOrStringOrFormData>
{
    private FileOrStringOrFormData(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="FileApi.File"/>, a <see cref="string"/> or a <see cref="Dom.FormData"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Converts a file to the union.</summary>
    [NotInIdl]
    public static implicit operator FileOrStringOrFormData(File value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a string to the union.</summary>
    [NotInIdl]
    public static implicit operator FileOrStringOrFormData(string value) => new(InfraString.ToScalarValueString(value ?? throw new ArgumentNullException(nameof(value))));

    /// <summary>Converts a form data to the union.</summary>
    [NotInIdl]
    public static implicit operator FileOrStringOrFormData(FormData value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from a file.</summary>
    [NotInIdl]
    public static FileOrStringOrFormData FromFile(File value) => value;

    /// <summary>Creates the union from a string.</summary>
    [NotInIdl]
    public static FileOrStringOrFormData FromString(string value) => value;

    /// <summary>Creates the union from a form data.</summary>
    [NotInIdl]
    public static FileOrStringOrFormData FromFormData(FormData value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(FileOrStringOrFormData other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FileOrStringOrFormData other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value?.ToString();

    /// <summary>Returns whether the two unions hold the same value.</summary>
    [NotInIdl]
    public static bool operator ==(FileOrStringOrFormData left, FileOrStringOrFormData right) => left.Equals(right);

    /// <summary>Returns whether the two unions hold different values.</summary>
    [NotInIdl]
    public static bool operator !=(FileOrStringOrFormData left, FileOrStringOrFormData right) => !left.Equals(right);
}
