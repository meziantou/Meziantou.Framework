using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://w3c.github.io/trusted-types/dist/spec/#trusted-type-policy-factory: the trusted type policy factory of a global
/// object (<see cref="Window.TrustedTypes"/>, <see cref="WorkerGlobalScope.TrustedTypes"/>). A document without a window
/// (e.g. a document parsed without a browsing context) is its own global object: see <see cref="Document.TrustedTypes"/>.
/// </summary>
[IdlInterface("TrustedTypePolicyFactory", "https://w3c.github.io/trusted-types/dist/spec/#trustedtypepolicyfactory")]
public sealed class TrustedTypePolicyFactory
{
    private readonly Window? _window;
    private readonly Document? _document;

    // https://w3c.github.io/trusted-types/dist/spec/#trustedtypepolicyfactory-created-policy-names
    private readonly List<string> _createdPolicyNames = [];

    internal TrustedTypePolicyFactory(Window window)
    {
        _window = window;
    }

    internal TrustedTypePolicyFactory(Document document)
    {
        _document = document;
    }

    /// <summary>
    /// The document whose content security policies apply: the associated document of the window, the environment document
    /// of the worker (<see cref="Document.WorkerGlobal"/>), or the document itself.
    /// </summary>
    internal Document Document => _window?.Document ?? _document!;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-createpolicy</summary>
    /// <exception cref="TypeErrorException">The creation of the policy is blocked by <see cref="Document.TrustedTypesDirectives"/>, or a default policy already exists.</exception>
    [IdlMember("createPolicy", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-createpolicy")]
    public TrustedTypePolicy CreatePolicy(string policyName, TrustedTypePolicyOptions? policyOptions = null)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        // https://w3c.github.io/trusted-types/dist/spec/#create-trusted-type-policy-algorithm
        if (TrustedTypesAlgorithms.ShouldPolicyCreationBeBlocked(this, policyName, _createdPolicyNames))
            throw new TypeErrorException($"The creation of the policy '{policyName}' is blocked by the trusted-types directive");

        if (policyName == "default" && DefaultPolicy is not null)
            throw new TypeErrorException("The default policy already exists");

        var policy = new TrustedTypePolicy(policyName, policyOptions);
        if (policyName == "default")
        {
            DefaultPolicy = policy;
        }

        _createdPolicyNames.Add(policyName);
        return policy;
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-ishtml</summary>
    [IdlMember("isHTML", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-ishtml")]
    public bool IsHtml(object? value) => value is TrustedHtml;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-isscript</summary>
    [IdlMember("isScript", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-isscript")]
    public bool IsScript(object? value) => value is TrustedScript;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-isscripturl</summary>
    [IdlMember("isScriptURL", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-isscripturl")]
    public bool IsScriptUrl(object? value) => value is TrustedScriptUrl;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-emptyhtml</summary>
    [IdlMember("emptyHTML", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-emptyhtml")]
    public TrustedHtml EmptyHtml { get; } = new("");

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-emptyscript</summary>
    [IdlMember("emptyScript", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-emptyscript")]
    public TrustedScript EmptyScript { get; } = new("");

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-defaultpolicy</summary>
    [IdlMember("defaultPolicy", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-defaultpolicy")]
    public TrustedTypePolicy? DefaultPolicy { get; private set; }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-getattributetype: the name of the trusted
    /// type required by a content attribute (<c>"TrustedHTML"</c>, <c>"TrustedScript"</c> or <c>"TrustedScriptURL"</c>), or null.
    /// </summary>
    [IdlMember("getAttributeType", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-getattributetype")]
    public string? GetAttributeType(string tagName, string attribute, string? elementNs = "", string? attrNs = "")
    {
        ArgumentNullException.ThrowIfNull(tagName);
        ArgumentNullException.ThrowIfNull(attribute);
        var localName = InfraAscii.ToAsciiLowercase(tagName);
        attribute = InfraAscii.ToAsciiLowercase(attribute);
        if (string.IsNullOrEmpty(elementNs))
        {
            elementNs = Namespaces.Html;
        }

        if (attrNs is { Length: 0 })
        {
            attrNs = null;
        }

        var elementInterface = TrustedTypesAlgorithms.GetElementInterface(localName, elementNs);
        return TrustedTypesAlgorithms.GetDataForAttribute(elementInterface, elementNs, attribute, attrNs) is { } data ? data.Type.GetIdlName() : null;
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-getpropertytype: the name of the trusted
    /// type required by an IDL attribute of an element (<c>"TrustedHTML"</c>, <c>"TrustedScript"</c> or <c>"TrustedScriptURL"</c>), or null.
    /// </summary>
    [IdlMember("getPropertyType", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyfactory-getpropertytype")]
    public string? GetPropertyType(string tagName, string property, string? elementNs = "")
    {
        ArgumentNullException.ThrowIfNull(tagName);
        ArgumentNullException.ThrowIfNull(property);
        var localName = InfraAscii.ToAsciiLowercase(tagName);
        if (string.IsNullOrEmpty(elementNs))
        {
            elementNs = Namespaces.Html;
        }

        TrustedTypeName? expectedType = (TrustedTypesAlgorithms.GetElementInterface(localName, elementNs), property) switch
        {
            (TrustedTypesElementInterface.HtmlIFrameElement, "srcdoc") => TrustedTypeName.TrustedHtml,
            (TrustedTypesElementInterface.HtmlScriptElement, "innerText" or "text" or "textContent") => TrustedTypeName.TrustedScript,
            (TrustedTypesElementInterface.HtmlScriptElement, "src") => TrustedTypeName.TrustedScriptUrl,
            (_, "innerHTML" or "outerHTML") => TrustedTypeName.TrustedHtml,
            _ => null,
        };

        return expectedType?.GetIdlName();
    }

    /// <summary>
    /// Returns the trusted type policy factory of the relevant global object of the objects of a document: the one of its
    /// window, or of the window of the document that created it (e.g. with <c>DOMParser</c>). A document without a window is
    /// its own global object.
    /// </summary>
    internal static TrustedTypePolicyFactory Get(Document document)
    {
        // The environment document of a worker represents its global object
        if (document.WorkerGlobal is { } worker)
            return worker.TrustedTypes;

        var originDocument = Document.GetOriginDocument(document);
        if (GetRelevantWindow(document, originDocument) is { } window)
            return window.TrustedTypes;

        return originDocument.OwnTrustedTypePolicyFactory;
    }

    /// <summary>The trusted types directives of the content security policies of the relevant global object of the objects of a document.</summary>
    internal static List<TrustedTypesDirectives>? GetDirectives(Document document) => GetGlobalDocument(document).TrustedTypesDirectivesOrNull;

    /// <summary>The document of the relevant global object of the objects of a document (the associated document of the window, the environment document of a worker, or the document that is its own global object), whose policies are violated.</summary>
    internal static Document GetGlobalDocument(Document document)
    {
        if (document.WorkerGlobal is not null)
            return document;

        var originDocument = Document.GetOriginDocument(document);
        return GetRelevantWindow(document, originDocument)?.Document ?? originDocument;
    }

    /// <summary>
    /// The relevant global object of the objects of a document: its window, else the window of its origin document. The
    /// template contents owner document is created in the realm of its document (https://html.spec.whatwg.org/multipage/scripting.html#appropriate-template-contents-owner-document),
    /// so the nodes of the template contents use the window of the document of the template.
    /// </summary>
    private static Window? GetRelevantWindow(Document document, Document originDocument)
        => document.RelevantWindow ?? (document.IsTemplateContentsOwnerDocument ? document.RealmWindow : null) ?? originDocument.RelevantWindow;
}
