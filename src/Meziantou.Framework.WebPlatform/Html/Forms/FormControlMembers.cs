namespace Meziantou.Framework.WebPlatform.Dom;

// The members shared by the form controls: the form owner, the labels, and the constraint validation API
// (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#the-constraint-validation-api).

public partial class HtmlButtonElement
{
    /// <summary>Gets the form owner of the element, or null when it has none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form")]
    public HtmlFormElement? Form => FormOwner;

    /// <summary>Gets the <c>label</c> elements associated with the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels"/>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels")]
    public NodeList Labels => GetLabels()!;

    /// <summary>Gets a value indicating whether the element is a candidate for constraint validation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate"/>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate")]
    public bool WillValidate => IsCandidateForConstraintValidation;

    /// <summary>Gets the <c>ValidityState</c> object that describes the validity of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity"/>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity")]
    public ValidityState Validity => GetValidity();

    /// <summary>
    /// Gets the error message that would be shown to the user if the element is invalid, or an empty string.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage"/>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage")]
    public string ValidationMessage => GetValidationMessage();

    /// <summary>
    /// Returns whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity")]
    public bool CheckValidity() => CheckValidityCore();

    /// <summary>
    /// Checks whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity")]
    public bool ReportValidity() => CheckValidityCore();

    /// <summary>
    /// Sets the custom validity error message of the element; an empty string clears the custom error.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity"/>
    [IdlMember("setCustomValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity")]
    public void SetCustomValidity(string error) => SetCustomValidityCore(error);
}

public partial class HtmlFieldSetElement
{
    /// <summary>Gets the form owner of the element, or null when it has none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form")]
    public HtmlFormElement? Form => FormOwner;

    /// <summary>Gets a value indicating whether the element is a candidate for constraint validation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate"/>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate")]
    public bool WillValidate => IsCandidateForConstraintValidation;

    /// <summary>Gets the <c>ValidityState</c> object that describes the validity of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity"/>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity")]
    public ValidityState Validity => GetValidity();

    /// <summary>
    /// Gets the error message that would be shown to the user if the element is invalid, or an empty string.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage"/>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage")]
    public string ValidationMessage => GetValidationMessage();

    /// <summary>
    /// Returns whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity")]
    public bool CheckValidity() => CheckValidityCore();

    /// <summary>
    /// Checks whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity")]
    public bool ReportValidity() => CheckValidityCore();

    /// <summary>
    /// Sets the custom validity error message of the element; an empty string clears the custom error.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity"/>
    [IdlMember("setCustomValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity")]
    public void SetCustomValidity(string error) => SetCustomValidityCore(error);
}

public partial class HtmlInputElement
{
    /// <summary>Gets the form owner of the element, or null when it has none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form")]
    public HtmlFormElement? Form => FormOwner;

    /// <summary>Gets the <c>label</c> elements associated with the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels"/>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels")]
    public NodeList? Labels => GetLabels();

    /// <summary>Gets a value indicating whether the element is a candidate for constraint validation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate"/>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate")]
    public bool WillValidate => IsCandidateForConstraintValidation;

    /// <summary>Gets the <c>ValidityState</c> object that describes the validity of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity"/>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity")]
    public ValidityState Validity => GetValidity();

    /// <summary>
    /// Gets the error message that would be shown to the user if the element is invalid, or an empty string.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage"/>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage")]
    public string ValidationMessage => GetValidationMessage();

    /// <summary>
    /// Returns whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity")]
    public bool CheckValidity() => CheckValidityCore();

    /// <summary>
    /// Checks whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity")]
    public bool ReportValidity() => CheckValidityCore();

    /// <summary>
    /// Sets the custom validity error message of the element; an empty string clears the custom error.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity"/>
    [IdlMember("setCustomValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity")]
    public void SetCustomValidity(string error) => SetCustomValidityCore(error);
}

public partial class HtmlObjectElement
{
    /// <summary>Gets the form owner of the element, or null when it has none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form")]
    public HtmlFormElement? Form => FormOwner;

    /// <summary>Gets a value indicating whether the element is a candidate for constraint validation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate"/>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate")]
    public bool WillValidate => IsCandidateForConstraintValidation;

    /// <summary>Gets the <c>ValidityState</c> object that describes the validity of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity"/>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity")]
    public ValidityState Validity => GetValidity();

    /// <summary>
    /// Gets the error message that would be shown to the user if the element is invalid, or an empty string.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage"/>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage")]
    public string ValidationMessage => GetValidationMessage();

    /// <summary>
    /// Returns whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity")]
    public bool CheckValidity() => CheckValidityCore();

    /// <summary>
    /// Checks whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity")]
    public bool ReportValidity() => CheckValidityCore();

    /// <summary>
    /// Sets the custom validity error message of the element; an empty string clears the custom error.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity"/>
    [IdlMember("setCustomValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity")]
    public void SetCustomValidity(string error) => SetCustomValidityCore(error);
}

public partial class HtmlOutputElement
{
    /// <summary>Gets the form owner of the element, or null when it has none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form")]
    public HtmlFormElement? Form => FormOwner;

    /// <summary>Gets the <c>label</c> elements associated with the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels"/>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels")]
    public NodeList Labels => GetLabels()!;

    /// <summary>Gets a value indicating whether the element is a candidate for constraint validation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate"/>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate")]
    public bool WillValidate => IsCandidateForConstraintValidation;

    /// <summary>Gets the <c>ValidityState</c> object that describes the validity of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity"/>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity")]
    public ValidityState Validity => GetValidity();

    /// <summary>
    /// Gets the error message that would be shown to the user if the element is invalid, or an empty string.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage"/>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage")]
    public string ValidationMessage => GetValidationMessage();

    /// <summary>
    /// Returns whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity")]
    public bool CheckValidity() => CheckValidityCore();

    /// <summary>
    /// Checks whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity")]
    public bool ReportValidity() => CheckValidityCore();

    /// <summary>
    /// Sets the custom validity error message of the element; an empty string clears the custom error.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity"/>
    [IdlMember("setCustomValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity")]
    public void SetCustomValidity(string error) => SetCustomValidityCore(error);
}

public partial class HtmlSelectElement
{
    /// <summary>Gets the form owner of the element, or null when it has none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form")]
    public HtmlFormElement? Form => FormOwner;

    /// <summary>Gets the <c>label</c> elements associated with the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels"/>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels")]
    public NodeList Labels => GetLabels()!;

    /// <summary>Gets a value indicating whether the element is a candidate for constraint validation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate"/>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate")]
    public bool WillValidate => IsCandidateForConstraintValidation;

    /// <summary>Gets the <c>ValidityState</c> object that describes the validity of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity"/>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity")]
    public ValidityState Validity => GetValidity();

    /// <summary>
    /// Gets the error message that would be shown to the user if the element is invalid, or an empty string.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage"/>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage")]
    public string ValidationMessage => GetValidationMessage();

    /// <summary>
    /// Returns whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity")]
    public bool CheckValidity() => CheckValidityCore();

    /// <summary>
    /// Checks whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity")]
    public bool ReportValidity() => CheckValidityCore();

    /// <summary>
    /// Sets the custom validity error message of the element; an empty string clears the custom error.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity"/>
    [IdlMember("setCustomValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity")]
    public void SetCustomValidity(string error) => SetCustomValidityCore(error);
}

public partial class HtmlTextAreaElement
{
    /// <summary>Gets the form owner of the element, or null when it has none.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-fae-form")]
    public HtmlFormElement? Form => FormOwner;

    /// <summary>Gets the <c>label</c> elements associated with the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels"/>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels")]
    public NodeList Labels => GetLabels()!;

    /// <summary>Gets a value indicating whether the element is a candidate for constraint validation.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate"/>
    [IdlMember("willValidate", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-willvalidate")]
    public bool WillValidate => IsCandidateForConstraintValidation;

    /// <summary>Gets the <c>ValidityState</c> object that describes the validity of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity"/>
    [IdlMember("validity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validity")]
    public ValidityState Validity => GetValidity();

    /// <summary>
    /// Gets the error message that would be shown to the user if the element is invalid, or an empty string.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage"/>
    [IdlMember("validationMessage", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-validationmessage")]
    public string ValidationMessage => GetValidationMessage();

    /// <summary>
    /// Returns whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-checkvalidity")]
    public bool CheckValidity() => CheckValidityCore();

    /// <summary>
    /// Checks whether the element satisfies its constraints, firing an <c>invalid</c> event when it does not.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-reportvalidity")]
    public bool ReportValidity() => CheckValidityCore();

    /// <summary>
    /// Sets the custom validity error message of the element; an empty string clears the custom error.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity"/>
    [IdlMember("setCustomValidity", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-cva-setcustomvalidity")]
    public void SetCustomValidity(string error) => SetCustomValidityCore(error);
}

public partial class HtmlMeterElement
{
    /// <summary>Gets the <c>label</c> elements associated with the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels"/>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels")]
    public NodeList Labels => GetLabels()!;
}

public partial class HtmlProgressElement
{
    /// <summary>Gets the <c>label</c> elements associated with the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels"/>
    [IdlMember("labels", "https://html.spec.whatwg.org/multipage/forms.html#dom-lfe-labels")]
    public NodeList Labels => GetLabels()!;
}
