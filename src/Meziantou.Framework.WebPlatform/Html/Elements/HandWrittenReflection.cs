using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// Reflected IDL attributes that are not annotated with [Reflect] in the IDL because their setter or getter has additional steps.

public partial class HtmlCanvasElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-width</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas is the placeholder of an <see cref="OffscreenCanvas"/>.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-width"/>
    [IdlMember("width", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-width")]
    public uint Width
    {
        get => Reflection.GetUnsignedLong(this, "width", positive: false, defaultValue: 300);
        set
        {
            ThrowIfPlaceholder();
            Reflection.SetUnsignedLong(this, "width", positive: false, positiveWithFallback: false, defaultValue: 300, value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-height</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the canvas is the placeholder of an <see cref="OffscreenCanvas"/>.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-height"/>
    [IdlMember("height", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvas-height")]
    public uint Height
    {
        get => Reflection.GetUnsignedLong(this, "height", positive: false, defaultValue: 150);
        set
        {
            ThrowIfPlaceholder();
            Reflection.SetUnsignedLong(this, "height", positive: false, positiveWithFallback: false, defaultValue: 150, value);
        }
    }

    /// <summary>The size of a placeholder canvas is the size of the frames of its <see cref="OffscreenCanvas"/>: its width and height cannot be set.</summary>
    private void ThrowIfPlaceholder()
    {
        if (IsPlaceholder)
            throw new DomException("The canvas has transferred its control to an OffscreenCanvas", "InvalidStateError");
    }
}

public partial class HtmlIFrameElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-srcdoc</summary>
    /// <exception cref="TypeErrorException">Trusted Types are enforced and the default policy did not convert the value.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-srcdoc"/>
    [IdlMember("srcdoc", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-srcdoc")]
    public string Srcdoc
    {
        get => Reflection.GetString(this, "srcdoc");
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Reflection.SetString(this, "srcdoc", TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, NodeDocument, value, "HTMLIFrameElement srcdoc"));
        }
    }

    /// <summary>The srcdoc setter with a <see cref="TrustedHtml"/> value (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-srcdoc).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-srcdoc"/>
    [IdlMember("srcdoc", "https://html.spec.whatwg.org/multipage/iframe-embed-object.html#dom-iframe-srcdoc")]
    public void SetSrcdoc(TrustedHtml value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Reflection.SetString(this, "srcdoc", TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedHtml, NodeDocument, value, "HTMLIFrameElement srcdoc"));
    }

    /// <summary>
    /// https://wicg.github.io/anonymous-iframe/#dom-htmliframeelement-credentialless: the documents of a credentialless
    /// iframe are loaded without credentials and are not subject to the embedder policy of the parent document.
    /// </summary>
    /// <seealso href="https://wicg.github.io/anonymous-iframe/#dom-htmliframeelement-credentialless"/>
    [IdlMember("credentialless", "https://wicg.github.io/anonymous-iframe/#dom-htmliframeelement-credentialless")]
    public bool Credentialless
    {
        get => Reflection.GetBoolean(this, "credentialless");
        set => Reflection.SetBoolean(this, "credentialless", value);
    }

    /// <summary>
    /// Gets the policy object of the iframe (https://w3c.github.io/webappsec-permissions-policy/#dom-htmliframeelement-permissionspolicy):
    /// the features that a document loaded into the iframe would be allowed to use, according to the permissions policy of
    /// the document of the iframe and to its <c>allow</c>, <c>allowfullscreen</c>, <c>src</c>, <c>srcdoc</c> and
    /// <c>sandbox</c> attributes.
    /// </summary>
    /// <seealso href="https://w3c.github.io/webappsec-permissions-policy/#dom-htmliframeelement-permissionspolicy"/>
    [IdlMember("permissionsPolicy", "https://w3c.github.io/webappsec-permissions-policy/#dom-htmliframeelement-permissionspolicy")]
    public PermissionsPolicy PermissionsPolicy => field ??= new PermissionsPolicy(this);
}

public partial class Document
{
    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-document-dir</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dom.html#dom-document-dir"/>
    [IdlMember("dir", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-dir")]
    public string Dir
    {
        get => DocumentElement is { IsHtmlNamespace: true, TagId: TagId.Html } html ? Reflection.GetEnumerated(html, "dir", EnumeratedAttributes.Dir) : "";
        set
        {
            if (DocumentElement is { IsHtmlNamespace: true, TagId: TagId.Html } html)
            {
                Reflection.SetString(html, "dir", value);
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-fgcolor</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-fgcolor"/>
    [IdlMember("fgColor", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-fgcolor")]
    public string FgColor
    {
        get => GetBodyAttribute("text");
        set => SetBodyAttribute("text", value);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-linkcolor</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-linkcolor"/>
    [IdlMember("linkColor", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-linkcolor")]
    public string LinkColor
    {
        get => GetBodyAttribute("link");
        set => SetBodyAttribute("link", value);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-vlinkcolor</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-vlinkcolor"/>
    [IdlMember("vlinkColor", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-vlinkcolor")]
    public string VlinkColor
    {
        get => GetBodyAttribute("vlink");
        set => SetBodyAttribute("vlink", value);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-alinkcolor</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-alinkcolor"/>
    [IdlMember("alinkColor", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-alinkcolor")]
    public string AlinkColor
    {
        get => GetBodyAttribute("alink");
        set => SetBodyAttribute("alink", value);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-bgcolor</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-bgcolor"/>
    [IdlMember("bgColor", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-document-bgcolor")]
    public string BgColor
    {
        get => GetBodyAttribute("bgcolor");
        set => SetBodyAttribute("bgcolor", value);
    }

    // The attributes reflect the content attributes of the body element; there is nothing to reflect when the body is a frameset
    private string GetBodyAttribute(string name) => Body is { IsHtmlNamespace: true, TagId: TagId.Body } body ? Reflection.GetString(body, name) : "";

    private void SetBodyAttribute(string name, string value)
    {
        if (Body is { IsHtmlNamespace: true, TagId: TagId.Body } body)
        {
            Reflection.SetString(body, name, value ?? "");
        }
    }
}
