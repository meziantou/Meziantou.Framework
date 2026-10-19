using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The <c>(RadioNodeList or Element)</c> union returned by the named getters of forms.</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlformcontrolscollection"/>
[IdlInterface("(RadioNodeList or Element)", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlformcontrolscollection")]
public readonly struct RadioNodeListOrElement : IUnionValue, IEquatable<RadioNodeListOrElement>
{
    private RadioNodeListOrElement(object value) => Value = value;

    /// <summary>Gets the value: a <c>RadioNodeList</c> or an <c>Element</c>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a <c>RadioNodeList</c>, or null when the value is an element.</summary>
    [NotInIdl]
    public RadioNodeList? RadioNodeList => Value as RadioNodeList;

    /// <summary>Gets the value as an <c>Element</c>, or null when the value is a node list.</summary>
    [NotInIdl]
    public Element? Element => Value as Element;

    /// <summary>Converts a <c>RadioNodeList</c> to the union.</summary>
    [NotInIdl]
    public static implicit operator RadioNodeListOrElement(RadioNodeList value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts an <c>Element</c> to the union.</summary>
    [NotInIdl]
    public static implicit operator RadioNodeListOrElement(Element value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from a <c>RadioNodeList</c>.</summary>
    [NotInIdl]
    public static RadioNodeListOrElement FromRadioNodeList(RadioNodeList value) => value;

    /// <summary>Creates the union from an <c>Element</c>.</summary>
    [NotInIdl]
    public static RadioNodeListOrElement FromElement(Element value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(RadioNodeListOrElement other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RadioNodeListOrElement other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(RadioNodeListOrElement left, RadioNodeListOrElement right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(RadioNodeListOrElement left, RadioNodeListOrElement right) => !left.Equals(right);
}

/// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlformcontrolscollection</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlformcontrolscollection"/>
[IdlInterface("HTMLFormControlsCollection", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#htmlformcontrolscollection")]
public sealed class HtmlFormControlsCollection : HtmlCollection
{
    private readonly Node _root;

    internal HtmlFormControlsCollection(Node root, Action<List<Element>> populate)
        : base(root, populate)
    {
        _root = root;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlformcontrolscollection-nameditem</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlformcontrolscollection-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlformcontrolscollection-nameditem")]
    public new RadioNodeListOrElement? NamedItem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
            return null;

        Element? first = null;
        var count = 0;
        foreach (var element in Elements)
        {
            if (element.IdValue == name || element.GetAttributeValue("name") == name)
            {
                first ??= element;
                count++;
            }
        }

        if (count == 0)
            return null;

        if (count == 1)
            return first!;

        return new RadioNodeList(_root, this, name);
    }

    /// <summary>
    /// Gets the element or the list of elements whose <c>id</c> or <c>name</c> matches the given name, or null when
    /// none matches.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlformcontrolscollection-nameditem"/>
    [IdlMember("namedItem", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-htmlformcontrolscollection-nameditem")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public new RadioNodeListOrElement? this[string name] => NamedItem(name);

    internal List<Element> GetElements() => Elements;
}

/// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#radionodelist</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#radionodelist"/>
[IdlInterface("RadioNodeList", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#radionodelist")]
public sealed class RadioNodeList : NodeList
{
    private readonly Node _root;
    private readonly Func<List<Element>> _source;
    private readonly string _name;

    internal RadioNodeList(Node root, HtmlFormControlsCollection collection, string name)
        : this(root, collection.GetElements, name)
    {
    }

    internal RadioNodeList(Node root, Func<List<Element>> source, string name)
    {
        _root = root;
        _source = source;
        _name = name;
    }

    private List<Element> Nodes => _source().FindAll(e => e.IdValue == _name || e.GetAttributeValue("name") == _name);

    /// <summary>Gets the number of nodes whose <c>id</c> or <c>name</c> matches the name of the list.</summary>
    public override uint Length => (uint)Nodes.Count;

    /// <summary>Returns the node at the given index, or null when the index is out of range.</summary>
    public override Node? Item(uint index)
    {
        var nodes = Nodes;
        return index < (uint)nodes.Count ? nodes[(int)index] : null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-radionodelist-value</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-radionodelist-value"/>
    [IdlMember("value", "https://html.spec.whatwg.org/multipage/common-dom-interfaces.html#dom-radionodelist-value")]
    public string Value
    {
        get
        {
            foreach (var node in Nodes)
            {
                if (node is HtmlInputElement { InputType: InputType.Radio, Checkedness: true } radio)
                    return radio.GetAttributeValue("value") ?? "on";
            }

            return "";
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var node in Nodes)
            {
                if (node is HtmlInputElement { InputType: InputType.Radio } radio && (radio.GetAttributeValue("value") ?? "on") == value)
                {
                    radio.Checked = true;
                    return;
                }
            }
        }
    }

    internal Node Root => _root;
}

public partial class HtmlFormElement
{
    private HtmlFormControlsCollection? _elements;
    private Dictionary<string, WeakReference<Element>>? _pastNames;

    // The RadioNodeList objects returned by the named getter
    private Dictionary<string, RadioNodeList>? _namedRadioNodeLists;
    private bool _constructingEntryList;
    private bool _firingSubmissionEvents;
    private bool _lockedForReset;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/forms.html#dom-form-elements: the listed elements whose form owner is the
    /// form, except image buttons, in tree order.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-elements"/>
    [IdlMember("elements", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-elements")]
    public HtmlFormControlsCollection Elements => _elements ??= new HtmlFormControlsCollection(this, list => PopulateElements(list, includeImageButtons: false));

    private void PopulateElements(List<Element> list, bool includeImageButtons)
    {
        var root = GetRoot();
        for (var node = root is Element rootElement ? rootElement : root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlElement { IsListed: true } element && ReferenceEquals(element.FormOwner, this) && (includeImageButtons || element is not HtmlInputElement { InputType: InputType.Image }))
            {
                list.Add(element);
            }
        }
    }

    /// <summary>Gets the number of listed form controls of the form.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-length"/>
    [IdlMember("length", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-length")]
    public uint Length => Elements.Length;

    /// <summary>Gets the listed form control at the given index, or null when the index is out of range.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-item"/>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-item")]
    public Element? this[uint index] => Elements.Item(index);

    /// <summary>The named getter (https://html.spec.whatwg.org/multipage/forms.html#dom-form-nameditem).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-nameditem"/>
    [IdlMember("getter", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-nameditem")]
    public RadioNodeListOrElement? this[string name] => GetNamedItem(name);

    // The candidates: the listed elements (except image buttons) and the img elements whose form owner is the form
    private List<Element> GetNamedCandidates(string name)
    {
        var result = new List<Element>();
        var root = GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlElement element && ReferenceEquals(element.FormOwner, this) && ((element.IsListed && element is not HtmlInputElement { InputType: InputType.Image }) || element.IsHtml(TagId.Img)))
            {
                if (element.IdValue == name || element.GetAttributeValue("name") == name)
                {
                    result.Add(element);
                }
            }
        }

        // Images are only candidates when there is no listed element
        if (result.Exists(e => !e.IsHtml(TagId.Img)))
        {
            result.RemoveAll(e => e.IsHtml(TagId.Img));
        }

        return result;
    }

    private RadioNodeListOrElement? GetNamedItem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var candidates = GetNamedCandidates(name);
        if (candidates.Count == 0)
        {
            if (_pastNames is not null && _pastNames.TryGetValue(name, out var reference) && reference.TryGetTarget(out var past) && IsInPastNamesMap(past))
                return past;

            return null;
        }

        // As in browsers, the same live RadioNodeList is returned for a name
        if (candidates.Count > 1)
        {
            _namedRadioNodeLists ??= new Dictionary<string, RadioNodeList>(StringComparer.Ordinal);
            if (!_namedRadioNodeLists.TryGetValue(name, out var list))
            {
                list = new RadioNodeList(this, () => GetNamedCandidates(name), name);
                _namedRadioNodeLists[name] = list;
            }

            return list;
        }

        (_pastNames ??= new Dictionary<string, WeakReference<Element>>(StringComparer.Ordinal))[name] = new WeakReference<Element>(candidates[0]);
        return candidates[0];
    }

    /// <summary>
    /// The entries of the past names map are removed when their element changes form owner
    /// (https://html.spec.whatwg.org/multipage/forms.html#past-names-map), e.g. when it is removed from the form.
    /// </summary>
    private bool IsInPastNamesMap(Element element) => element is HtmlElement htmlElement && ReferenceEquals(htmlElement.FormOwner, this);

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#the-form-element:supported-property-names</summary>
    internal IReadOnlyList<string> GetSupportedPropertyNames()
    {
        var names = new List<string>();
        var root = GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlElement element && ReferenceEquals(element.FormOwner, this) && ((element.IsListed && element is not HtmlInputElement { InputType: InputType.Image }) || element.IsHtml(TagId.Img)))
            {
                if (element.IdValue is { Length: > 0 } id && !names.Contains(id))
                {
                    names.Add(id);
                }

                if (element.GetAttributeValue("name") is { Length: > 0 } elementName && !names.Contains(elementName))
                {
                    names.Add(elementName);
                }
            }
        }

        if (_pastNames is not null)
        {
            foreach (var (name, reference) in _pastNames)
            {
                if (reference.TryGetTarget(out var past) && IsInPastNamesMap(past) && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#dom-form-submit</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-submit"/>
    [IdlMember("submit", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-submit")]
    public void Submit() => SubmitForm(this, submittedFromSubmitMethod: true);

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#dom-form-requestsubmit</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-requestsubmit"/>
    [IdlMember("requestSubmit", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-requestsubmit")]
    public void RequestSubmit(HtmlElement? submitter = null)
    {
        if (submitter is not null)
        {
            if (!submitter.IsSubmitButton())
                throw new TypeErrorException("The submitter is not a submit button");

            if (!ReferenceEquals(submitter.FormOwner, this))
                throw DomException.NotFound("The submitter is not owned by the form");
        }

        SubmitForm((HtmlElement?)submitter ?? this, submittedFromSubmitMethod: false);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#dom-form-reset</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-reset"/>
    [IdlMember("reset", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-reset")]
    public void Reset()
    {
        using var ceReactions = CEReactionsScope.Enter();
        if (_lockedForReset)
            return;

        _lockedForReset = true;
        try
        {
            ResetForm();
        }
        finally
        {
            _lockedForReset = false;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-form-reset</summary>
    internal void ResetForm()
    {
        var reset = FireEvent(new Event("reset", new EventInit { Bubbles = true, Cancelable = true }));
        if (!reset)
            return;

        var controls = new List<HtmlElement>();
        var root = GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlElement { IsResettable: true } element && ReferenceEquals(element.FormOwner, this))
            {
                controls.Add(element);
            }
        }

        foreach (var control in controls)
        {
            control.ResetAlgorithm();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#dom-form-checkvalidity</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-checkvalidity"/>
    [IdlMember("checkValidity", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-checkvalidity")]
    public bool CheckValidity() => StaticallyValidate(fireInvalidEvents: true);

    /// <summary>
    /// Checks the validity of the form controls, fires an <c>invalid</c> event on each invalid control and returns
    /// whether all of them are valid.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-form-reportvalidity"/>
    [IdlMember("reportValidity", "https://html.spec.whatwg.org/multipage/forms.html#dom-form-reportvalidity")]
    public bool ReportValidity() => StaticallyValidate(fireInvalidEvents: true);

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#statically-validate-the-constraints</summary>
    private bool StaticallyValidate(bool fireInvalidEvents)
    {
        var invalid = new List<HtmlElement>();
        var root = GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlElement { IsSubmittable: true } element && ReferenceEquals(element.FormOwner, this) && element.IsCandidateForConstraintValidation && !element.SatisfiesConstraints)
            {
                invalid.Add(element);
            }
        }

        if (invalid.Count == 0)
            return true;

        if (fireInvalidEvents)
        {
            foreach (var element in invalid)
            {
                element.FireEvent(new Event("invalid", new EventInit { Cancelable = true }));
            }
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#cannot-navigate</summary>
    private bool CannotNavigate => !IsConnected || !NodeDocument.IsFullyActive;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-form-submit: the form data is encoded
    /// and the target navigable is navigated (a GET or POST request given to the <see cref="DocumentLoader"/>). The targets
    /// that need a new navigable (<c>_blank</c>, unknown names) are ignored.
    /// </summary>
    internal void SubmitForm(HtmlElement submitter, bool submittedFromSubmitMethod)
    {
        if (CannotNavigate || _constructingEntryList)
            return;

        var formDocument = NodeDocument;
        if (HasSandboxedFormsFlag(formDocument))
            return;

        if (!submittedFromSubmitMethod)
        {
            if (_firingSubmissionEvents)
                return;

            _firingSubmissionEvents = true;
            try
            {
                // The user validity of the submittable elements of the form becomes true (:user-valid and :user-invalid)
                var root = GetRoot();
                for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
                {
                    if (node is HtmlElement { IsSubmittable: true } field && ReferenceEquals(field.FormOwner, this))
                    {
                        field.UserValidity = true;
                    }
                }

                // Interactively validate the constraints, unless the submitter has no-validate state
                var noValidate = (submitter is not HtmlFormElement && submitter.HasAttributeWithLocalName("formnovalidate")) || HasAttributeWithLocalName("novalidate");
                if (!noValidate && !StaticallyValidate(fireInvalidEvents: true))
                    return;

                var submitterButton = ReferenceEquals(submitter, this) ? null : submitter;
                var shouldContinue = FireEvent(new SubmitEvent("submit", new SubmitEventInit { Bubbles = true, Cancelable = true, Submitter = submitterButton }));
                if (!shouldContinue)
                    return;
            }
            finally
            {
                _firingSubmissionEvents = false;
            }
        }

        if (CannotNavigate)
            return;

        var encoding = PickEncoding();
        var entryList = ConstructEntryList(ReferenceEquals(submitter, this) ? null : submitter, encoding);
        if (entryList is null || CannotNavigate)
            return;

        var method = GetSubmitterAttributeState(submitter, "formmethod", "method", EnumeratedAttributes.Formmethod, EnumeratedAttributes.Method);
        if (method == "dialog")
        {
            if (GetAncestorDialog() is not { } subject)
                return;

            string? result = null;
            if (submitter is HtmlInputElement { InputType: InputType.Image } imageButton)
            {
                result = string.Create(CultureInfo.InvariantCulture, $"{imageButton.SelectedCoordinate.X},{imageButton.SelectedCoordinate.Y}");
            }
            else if (submitter.IsSubmitButton())
            {
                result = submitter.GetAttributeValue("value");
            }
            else
            {
                // Not in the spec (the result is null when the submitter is the form), but as in the browsers: the dialog
                // closed by form.submit() gets an empty return value
                result = "";
            }

            subject.Close(result);
            return;
        }

        var action = submitter is not HtmlFormElement && submitter.IsSubmitButton() && submitter.GetAttributeValue("formaction") is { } formAction ? formAction : GetAttributeValue("action") ?? "";
        if (action.Length == 0)
        {
            action = formDocument.Url;
        }

        if (Reflection.EncodingParseUrl(submitter.NodeDocument, action) is not { } parsedAction)
            return;

        var enctype = GetSubmitterAttributeState(submitter, "formenctype", "enctype", EnumeratedAttributes.Formenctype, EnumeratedAttributes.Enctype);
        var formTarget = submitter is not HtmlFormElement && submitter.IsSubmitButton() ? submitter.GetAttributeValue("formtarget") : null;
        var target = GetElementTarget(formTarget);
        if (formDocument.BrowsingContext?.ChooseNavigable(target, HyperlinkElementUtils.GetNoopener(this, target), out _) is not { } targetNavigable)
            return;

        var historyHandling = ReferenceEquals(formDocument, targetNavigable.Document) && !formDocument.CompletelyLoaded ? NavigationHistoryBehavior.Replace : NavigationHistoryBehavior.Auto;
        var scheme = parsedAction.Scheme;
        var isPost = method == "post";
        switch (scheme)
        {
            case "http" or "https" when !isPost:
            case "data" when !isPost:
                // Mutate action URL
                parsedAction.Query = FormSubmissionEncoding.SerializeUrlEncoded(FormSubmissionEncoding.ConvertToNameValuePairs(entryList), encoding);
                PlanToNavigate(parsedAction, postResource: null);
                break;

            case "http" or "https":
            {
                // Submit as entity body
                PostResource postResource;
                if (enctype == "multipart/form-data")
                {
                    var (body, boundary) = FormSubmissionEncoding.SerializeMultipart(entryList, encoding);
                    postResource = new PostResource(body, "multipart/form-data; boundary=" + boundary);
                }
                else if (enctype == "text/plain")
                {
                    var text = FormSubmissionEncoding.SerializeTextPlain(FormSubmissionEncoding.ConvertToNameValuePairs(entryList));
                    postResource = new PostResource(FormSubmissionEncoding.Encode(text, encoding), "text/plain");
                }
                else
                {
                    var text = FormSubmissionEncoding.SerializeUrlEncoded(FormSubmissionEncoding.ConvertToNameValuePairs(entryList), encoding);
                    postResource = new PostResource(System.Text.Encoding.ASCII.GetBytes(text), "application/x-www-form-urlencoded");
                }

                PlanToNavigate(parsedAction, postResource);
                break;
            }

            case "mailto" when !isPost:
            {
                // Mail with headers
                var headers = FormSubmissionEncoding.SerializeUrlEncoded(FormSubmissionEncoding.ConvertToNameValuePairs(entryList), encoding).Replace("+", "%20", StringComparison.Ordinal);
                parsedAction.Query = headers;
                PlanToNavigate(parsedAction, postResource: null);
                break;
            }

            case "mailto":
            {
                // Mail as body
                var pairs = FormSubmissionEncoding.ConvertToNameValuePairs(entryList);
                var body = enctype == "text/plain"
                    ? PercentEncoding.Utf8PercentEncode(FormSubmissionEncoding.SerializeTextPlain(pairs), PercentEncodeSet.Path)
                    : FormSubmissionEncoding.SerializeUrlEncoded(pairs, encoding);
                var query = parsedAction.Query ?? "";
                if (query.Length > 0)
                {
                    query += "&";
                }

                parsedAction.Query = query + "body=" + body;
                PlanToNavigate(parsedAction, postResource: null);
                break;
            }

            default:
                // Get action URL (ftp, javascript, data with POST, and the other schemes)
                PlanToNavigate(parsedAction, postResource: null);
                break;
        }

        // https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#plan-to-navigate
        void PlanToNavigate(UrlRecord url, PostResource? postResource)
        {
            var referrerPolicy = HasLinkType("noreferrer") ? "no-referrer" : "";

            // The navigate event gets the submitter and the entry list of a POST submission (the navigation gets the entry list
            // of all the submissions)
            var userInvolvement = submitter.NodeDocument.RelevantWindow?.HasTransientActivation == true ? UserNavigationInvolvement.Activation : UserNavigationInvolvement.None;
            var formData = new FormData(entryList);
            // A new plan cancels the planned navigation. As in browsers, the submit() method only cancels the planned navigation
            // of the same target navigable, so a form submitted to several targets in succession navigates all of them (a
            // submission that fires the submit event, e.g. the activation of a submit button, cancels all of them)
            _plannedNavigations ??= [];
            foreach (var planned in _plannedNavigations)
            {
                if (!submittedFromSubmitMethod || ReferenceEquals(planned.Target, targetNavigable))
                {
                    planned.Canceled = true;
                }
            }

            _plannedNavigations.RemoveAll(planned => planned.Canceled);
            targetNavigable.CancelPendingJavaScriptUrlNavigations();
            var plannedNavigation = new PlannedNavigation(targetNavigable);
            _plannedNavigations.Add(plannedNavigation);
            var lastNavigationId = BrowsingContext.LastNavigationId;
            var stopCount = targetNavigable.StopCount;
            NodeDocument.QueueElementTask(() =>
            {
                // A new plan, or window.stop() in the target navigable (as in browsers, https://github.com/whatwg/html/issues/3730), cancels the planned navigation
                if (plannedNavigation.Canceled || targetNavigable.StopCount != stopCount)
                    return;

                plannedNavigation.Canceled = true;
                _plannedNavigations.Remove(plannedNavigation);

                // As in browsers, a navigation of the target navigable that started after the submission (e.g. a location
                // navigation) takes precedence
                if (targetNavigable.OngoingNavigationId > lastNavigationId)
                    return;

                targetNavigable.Navigate(url, formDocument, historyHandling, referrerPolicy: referrerPolicy, postResource: postResource, userInvolvement: userInvolvement, sourceElement: submitter, formData: formData);
            });
        }
    }

    /// <summary>The planned navigations of the form (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#planned-navigation), one per target navigable.</summary>
    private List<PlannedNavigation>? _plannedNavigations;

    private sealed class PlannedNavigation(BrowsingContext target)
    {
        public BrowsingContext Target { get; } = target;

        public bool Canceled { get; set; }
    }

    /// <summary>
    /// The method or the enctype of the submitter element (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-fs-method):
    /// the state of the attribute of the submit button (formmethod, formenctype), else of the form (method, enctype).
    /// </summary>
    private string GetSubmitterAttributeState(HtmlElement submitter, string buttonAttribute, string formAttribute, EnumeratedAttribute buttonTable, EnumeratedAttribute formTable)
    {
        if (submitter is not HtmlFormElement && submitter.IsSubmitButton() && submitter.GetAttributeValue(buttonAttribute) is { } buttonValue)
        {
            var buttonState = buttonTable.GetState(buttonValue);
            if (buttonState >= 0)
                return buttonTable.CanonicalKeywords[buttonState]!;
        }

        var state = formTable.GetState(GetAttributeValue(formAttribute));
        return formTable.CanonicalKeywords[state]!;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#picking-an-encoding-for-the-form</summary>
    private WebEncodingId PickEncoding()
    {
        var encoding = NodeDocument.Encoding;
        if (GetAttributeValue("accept-charset") is { } acceptCharset)
        {
            encoding = WebEncodingId.Utf8;
            foreach (var label in acceptCharset.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (WebEncodingRegistry.TryGetEncoding(label, out var candidate))
                {
                    encoding = candidate;
                    break;
                }
            }
        }

        return WebEncodingRegistry.GetOutputEncoding(encoding);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/semantics.html#get-an-element's-target, given a target override (the formtarget of the submitter).</summary>
    private string GetElementTarget(string? target)
    {
        // An empty target uses the target of the base element, as in the browsers (the specification uses the empty string,
        // which chooses the current navigable)
        target ??= GetAttributeValue("target");
        if (string.IsNullOrEmpty(target))
        {
            foreach (var descendant in NodeDocument.Descendants())
            {
                if (descendant is HtmlBaseElement baseElement && baseElement.GetAttributeValue("target") is { } baseTarget)
                {
                    target = baseTarget;
                    break;
                }
            }
        }

        if (target is null)
            return "";

        if (target.AsSpan().IndexOfAny("\t\n\r") >= 0 && target.Contains('<', StringComparison.Ordinal))
            return "_blank";

        return target;
    }

    private bool HasLinkType(string linkType)
    {
        var rel = GetAttributeValue("rel");
        if (rel is null)
            return false;

        foreach (var token in rel.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(token, linkType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private HtmlDialogElement? GetAncestorDialog()
    {
        for (var ancestor = ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor is HtmlDialogElement dialog)
                return dialog;
        }

        return null;
    }

    /// <summary>
    /// Whether the active sandboxing flag set of the document has the sandboxed forms browsing context flag
    /// (https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-forms-browsing-context-flag): the sandbox attribute of
    /// its iframe does not have the allow-forms keyword.
    /// </summary>
    private static bool HasSandboxedFormsFlag(Document document)
    {
        if (document.BrowsingContext?.Container is not HtmlIFrameElement iframe || iframe.GetAttributeValue("sandbox") is not { } sandbox)
            return false;

        foreach (var token in sandbox.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("allow-forms", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#constructing-the-form-data-set</summary>
    internal List<KeyValuePair<string, FormDataEntryValue>>? ConstructEntryList(HtmlElement? submitter, WebEncodingId encoding = WebEncodingId.Utf8)
    {
        if (_constructingEntryList)
            return null;

        _constructingEntryList = true;
        try
        {
            var entries = new List<KeyValuePair<string, FormDataEntryValue>>();
            var controls = new List<HtmlElement>();
            var root = GetRoot();
            for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
            {
                if (node is HtmlElement { IsSubmittable: true } element && ReferenceEquals(element.FormOwner, this))
                {
                    controls.Add(element);
                }
            }

            foreach (var field in controls)
            {
                // Skip the controls with a datalist ancestor, the disabled controls, and the buttons that are not the submitter
                if (IsInDatalist(field) || Css.Selectors.HtmlElementStates.IsActuallyDisabled(field))
                    continue;

                if (field.IsButtonForSubmission() && !ReferenceEquals(field, submitter))
                    continue;

                if (field is HtmlInputElement { InputType: InputType.Checkbox or InputType.Radio, Checkedness: false })
                    continue;

                if (field is HtmlInputElement { InputType: InputType.Image } image)
                {
                    var prefix = image.GetAttributeValue("name") is { Length: > 0 } imageName ? imageName + "." : "";
                    entries.Add(FormData.CreateEntry(prefix + "x", image.SelectedCoordinate.X.ToString(CultureInfo.InvariantCulture)));
                    entries.Add(FormData.CreateEntry(prefix + "y", image.SelectedCoordinate.Y.ToString(CultureInfo.InvariantCulture)));
                    continue;
                }

                if (field.IsFormAssociatedCustomElement)
                {
                    field.AppendFormAssociatedCustomElementEntries(entries);
                    continue;
                }

                var name = field.GetAttributeValue("name");
                if (string.IsNullOrEmpty(name))
                    continue;

                switch (field)
                {
                    case HtmlSelectElement select:
                        foreach (var option in select.GetListOfOptions())
                        {
                            if (option.Selectedness && !option.IsDisabled)
                            {
                                entries.Add(FormData.CreateEntry(name, option.Value));
                            }
                        }

                        break;

                    case HtmlInputElement { InputType: InputType.Checkbox or InputType.Radio } checkable:
                        entries.Add(FormData.CreateEntry(name, checkable.GetAttributeValue("value") ?? "on"));
                        break;

                    case HtmlInputElement { InputType: InputType.File } fileInput:
                        // An entry for each selected file, or an empty file when no file is selected
                        if (fileInput.SelectedFiles.Count == 0)
                        {
                            entries.Add(new(InfraString.ToScalarValueString(name), new File([], "", new FilePropertyBag { Type = "application/octet-stream" })));
                        }
                        else
                        {
                            foreach (var file in fileInput.SelectedFiles)
                            {
                                entries.Add(new(InfraString.ToScalarValueString(name), file));
                            }
                        }

                        break;

                    case HtmlInputElement { InputType: InputType.Hidden } hidden when InfraAscii.EqualsIgnoreAsciiCase(name, "_charset_"):
                        entries.Add(FormData.CreateEntry(name, WebEncodingRegistry.GetName(encoding)));
                        break;

                    case HtmlInputElement input:
                        entries.Add(FormData.CreateEntry(name, input.Value));
                        break;

                    case HtmlTextAreaElement textArea:
                        entries.Add(FormData.CreateEntry(name, textArea.Value));
                        break;

                    case HtmlButtonElement button:
                        entries.Add(FormData.CreateEntry(name, button.Value));
                        break;
                }

                // The dirname attribute (when it applies to the type of the input): the directionality of the element
                if (field is HtmlInputElement { DirNameApplies: true } or HtmlTextAreaElement && field.GetAttributeValue("dirname") is { Length: > 0 } dirname)
                {
                    entries.Add(FormData.CreateEntry(dirname, Css.Selectors.HtmlElementStates.IsLeftToRight(field) ? "ltr" : "rtl"));
                }
            }

            // Fire the formdata event
            var formData = new FormData(entries);
            FireEvent(new FormDataEvent("formdata", new FormDataEventInit { Bubbles = true, FormData = formData }));
            return formData.Entries;
        }
        finally
        {
            _constructingEntryList = false;
        }
    }

    private static bool IsInDatalist(Element element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (ancestor.IsHtml(TagId.Datalist))
                return true;
        }

        return false;
    }

    /// <summary>
    /// When a form is removed, the elements associated with it by the parser that are no longer in its tree get a new form owner.
    /// </summary>
    internal override void RemovingSteps(bool isSubtreeRoot, Node oldAncestor)
    {
        base.RemovingSteps(isSubtreeRoot, oldAncestor);
        _pastNames?.Clear();
    }
}

public partial class HtmlElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#concept-submit-button</summary>
    internal bool IsSubmitButton() => this switch
    {
        HtmlButtonElement button => button.IsButtonSubmitButton,
        HtmlInputElement input => input.InputType is InputType.Submit or InputType.Image,
        _ => false,
    };

    // The buttons whose value is only submitted when they are the submitter
    internal bool IsButtonForSubmission() => this switch
    {
        HtmlButtonElement => true,
        HtmlInputElement input => input.InputType is InputType.Submit or InputType.Image or InputType.Reset or InputType.Button,
        _ => false,
    };

    /// <summary>The reset algorithm of resettable elements (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-form-reset-control).</summary>
    internal virtual void ResetAlgorithm()
    {
        // https://html.spec.whatwg.org/multipage/custom-elements.html#form-associated-custom-elements: the reset algorithm enqueues formResetCallback
        if (IsFormAssociatedCustomElement && IsCustom)
        {
            CustomElementReactions.EnqueueCallbackReaction(this, CustomElementCallbackKind.FormReset, []);
        }
    }
}
