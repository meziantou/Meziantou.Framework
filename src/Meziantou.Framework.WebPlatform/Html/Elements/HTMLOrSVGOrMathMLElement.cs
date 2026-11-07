using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#focusoptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#focusoptions"/>
[IdlInterface("FocusOptions", "https://html.spec.whatwg.org/multipage/interaction.html#focusoptions")]
public sealed class FocusOptions
{
    /// <summary>
    /// Gets a value indicating whether the user agent must not scroll the element into view when it is focused.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-focusoptions-preventscroll"/>
    [IdlMember("preventScroll", "https://html.spec.whatwg.org/multipage/interaction.html#dom-focusoptions-preventscroll")]
    public bool PreventScroll { get; init; }

    /// <summary>
    /// Gets a value indicating whether the focus indicator must be visible, or null to let the user agent decide.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-focusoptions-focusvisible"/>
    [IdlMember("focusVisible", "https://html.spec.whatwg.org/multipage/interaction.html#dom-focusoptions-focusvisible")]
    public bool? FocusVisible { get; init; }
}

// The HTMLOrSVGOrMathMLElement mixin (https://html.spec.whatwg.org/multipage/dom.html#htmlorsvgormathmlelement)
public partial class Element
{
    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-dataset</summary>
    internal DomStringMap GetDataset() => ElementRareData.Dataset ??= new DomStringMap(this);

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dom-noncedelement-nonce</summary>
    internal string CryptographicNonce
    {
        get => (RareDataOrNull as ElementRareData)?.CryptographicNonce ?? "";
        set => ElementRareData.CryptographicNonce = value;
    }

    /// <summary>
    /// The steps run when an element with an autofocus attribute is inserted into a document
    /// (https://html.spec.whatwg.org/multipage/interaction.html#the-autofocus-attribute): it becomes an autofocus candidate.
    /// </summary>
    internal override void InsertionSteps()
    {
        base.InsertionSteps();
        if (IsConnected && HasAttributeWithLocalName("autofocus") && (IsHtmlNamespace || ReferenceEquals(NamespaceUri, Namespaces.Svg) || ReferenceEquals(NamespaceUri, Namespaces.MathML)))
        {
            FocusModel.OnAutofocusElementInserted(this);
        }
    }

    /// <summary>
    /// The nonce is hidden when the element becomes browsing-context connected (see <see cref="HideNonce"/>), in the
    /// post-connection steps, as in the browsers: after the <c>connectedCallback</c> reaction of a custom element is enqueued.
    /// </summary>
    internal override void PostConnectionSteps()
    {
        base.PostConnectionSteps();
        if (RareDataOrNull is ElementRareData { CryptographicNonce.Length: > 0 })
        {
            HideNonce();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#nonce-attributes: when an element becomes
    /// browsing-context connected and the CSP list of its document contains a header-delivered policy, its <c>nonce</c>
    /// content attribute is set to the empty string (its cryptographic nonce, <c>element.nonce</c>, is kept), so that the
    /// nonce is not exposed to the side-channels like the CSS attribute selectors. As in the browsers, the parser also hides
    /// the nonce added to the <c>html</c> and <c>body</c> elements by a dangling start tag.
    /// </summary>
    internal void HideNonce()
    {
        if (this is not (HtmlElement or SvgElement or MathMLElement) || NodeDocument is not { BrowsingContext: not null, ContentSecurityPoliciesOrNull: { } policies } || !policies.Exists(policy => policy.IsHeaderDelivered))
            return;

        if (GetAttributeValue("nonce") is not { Length: > 0 })
            return;

        var nonce = CryptographicNonce;
        SetAttributeValue("nonce", "");
        CryptographicNonce = nonce;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#nonce-attributes: the cloning steps copy the nonce.</summary>
    internal override void CloningSteps(Node copy, bool subtree)
    {
        base.CloningSteps(copy, subtree);
        if ((RareDataOrNull as ElementRareData)?.CryptographicNonce is { } nonce)
        {
            ((Element)copy).CryptographicNonce = nonce;
        }
    }
}

public partial class HtmlElement
{
    /// <summary>Gets the <c>data-*</c> custom data attributes of the element as a string map.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-dataset"/>
    [IdlMember("dataset", "https://html.spec.whatwg.org/multipage/dom.html#dom-dataset")]
    public DomStringMap Dataset => GetDataset();

    /// <summary>
    /// Gets or sets the cryptographic nonce used by the Content Security Policy checks of the element.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dom-noncedelement-nonce"/>
    [IdlMember("nonce", "https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dom-noncedelement-nonce")]
    public string Nonce
    {
        get => CryptographicNonce;
        set => CryptographicNonce = value;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#dom-focus: runs the focusing steps for the element (see
    /// <see cref="Document.ActiveElement"/>). Nothing is scrolled.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-focus"/>
    [IdlMember("focus", "https://html.spec.whatwg.org/multipage/interaction.html#dom-focus")]
    public void Focus(FocusOptions? options = null) => FocusModel.FocusElement(this, options);

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-blur: runs the unfocusing steps for the element (the viewport is focused).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-blur"/>
    [IdlMember("blur", "https://html.spec.whatwg.org/multipage/interaction.html#dom-blur")]
    public void Blur() => FocusModel.RunUnfocusingSteps(this);
}

public partial class SvgElement
{
    /// <summary>Gets the <c>data-*</c> custom data attributes of the element as a string map.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-dataset"/>
    [IdlMember("dataset", "https://html.spec.whatwg.org/multipage/dom.html#dom-dataset")]
    public DomStringMap Dataset => GetDataset();

    /// <summary>
    /// Gets or sets the cryptographic nonce used by the Content Security Policy checks of the element.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dom-noncedelement-nonce"/>
    [IdlMember("nonce", "https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dom-noncedelement-nonce")]
    public string Nonce
    {
        get => CryptographicNonce;
        set => CryptographicNonce = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-focus: runs the focusing steps for the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-focus"/>
    [IdlMember("focus", "https://html.spec.whatwg.org/multipage/interaction.html#dom-focus")]
    public void Focus(FocusOptions? options = null) => FocusModel.FocusElement(this, options);

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-blur: runs the unfocusing steps for the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-blur"/>
    [IdlMember("blur", "https://html.spec.whatwg.org/multipage/interaction.html#dom-blur")]
    public void Blur() => FocusModel.RunUnfocusingSteps(this);
}

public partial class MathMLElement
{
    /// <summary>Gets the <c>data-*</c> custom data attributes of the element as a string map.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-dataset"/>
    [IdlMember("dataset", "https://html.spec.whatwg.org/multipage/dom.html#dom-dataset")]
    public DomStringMap Dataset => GetDataset();

    /// <summary>
    /// Gets or sets the cryptographic nonce used by the Content Security Policy checks of the element.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dom-noncedelement-nonce"/>
    [IdlMember("nonce", "https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dom-noncedelement-nonce")]
    public string Nonce
    {
        get => CryptographicNonce;
        set => CryptographicNonce = value;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-focus: runs the focusing steps for the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-focus"/>
    [IdlMember("focus", "https://html.spec.whatwg.org/multipage/interaction.html#dom-focus")]
    public void Focus(FocusOptions? options = null) => FocusModel.FocusElement(this, options);

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-blur: runs the unfocusing steps for the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-blur"/>
    [IdlMember("blur", "https://html.spec.whatwg.org/multipage/interaction.html#dom-blur")]
    public void Blur() => FocusModel.RunUnfocusingSteps(this);
}
