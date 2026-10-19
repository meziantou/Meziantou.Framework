using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class HtmlAnchorElement
{
    /// <summary>
    /// Gets the serialization of the origin of the URL of the hyperlink, or an empty string when there is no URL.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin")]
    public string Origin => HyperlinkElementUtils.Get(this, static url => url.Origin, "");

    /// <summary>Gets or sets the scheme of the URL of the hyperlink, followed by a colon.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol"/>
    [IdlMember("protocol", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol")]
    public string Protocol
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Protocol, ":");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Protocol = value);
    }

    /// <summary>Gets or sets the username of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username"/>
    [IdlMember("username", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username")]
    public string Username
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Username, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Username = value);
    }

    /// <summary>Gets or sets the password of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password"/>
    [IdlMember("password", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password")]
    public string Password
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Password, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Password = value);
    }

    /// <summary>Gets or sets the host and the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host"/>
    [IdlMember("host", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host")]
    public string Host
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Host, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Host = value);
    }

    /// <summary>Gets or sets the host of the URL of the hyperlink, without the port.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname"/>
    [IdlMember("hostname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname")]
    public string Hostname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hostname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hostname = value);
    }

    /// <summary>Gets or sets the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port"/>
    [IdlMember("port", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port")]
    public string Port
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Port, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Port = value);
    }

    /// <summary>Gets or sets the path of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname"/>
    [IdlMember("pathname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname")]
    public string Pathname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Pathname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Pathname = value);
    }

    /// <summary>Gets or sets the query of the URL of the hyperlink, including the leading question mark.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search"/>
    [IdlMember("search", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search")]
    public string Search
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Search, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Search = value);
    }

    /// <summary>Gets or sets the fragment of the URL of the hyperlink, including the leading number sign.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash"/>
    [IdlMember("hash", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash")]
    public string Hash
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hash, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hash = value);
    }

    /// <summary>The stringifier of HTMLHyperlinkElementUtils: the href IDL attribute.</summary>
    [NotInIdl("The stringifier of the href IDL attribute")]
    public override string ToString() => Href;

    // The hyperlinks without an href attribute have no activation behavior (they are not the activation target of a click)
    internal override bool HasActivationBehavior => HasAttribute("href");

    internal override void ActivationBehavior(Event @event)
    {
        // Not in the spec, but as in the browsers: an editable link does not follow its hyperlink
        if (IsContentEditable)
            return;

        HyperlinkElementUtils.ActivationBehavior(this);
    }
}

public partial class HtmlAreaElement
{
    /// <summary>
    /// Gets the serialization of the origin of the URL of the hyperlink, or an empty string when there is no URL.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin")]
    public string Origin => HyperlinkElementUtils.Get(this, static url => url.Origin, "");

    /// <summary>Gets or sets the scheme of the URL of the hyperlink, followed by a colon.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol"/>
    [IdlMember("protocol", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol")]
    public string Protocol
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Protocol, ":");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Protocol = value);
    }

    /// <summary>Gets or sets the username of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username"/>
    [IdlMember("username", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username")]
    public string Username
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Username, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Username = value);
    }

    /// <summary>Gets or sets the password of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password"/>
    [IdlMember("password", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password")]
    public string Password
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Password, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Password = value);
    }

    /// <summary>Gets or sets the host and the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host"/>
    [IdlMember("host", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host")]
    public string Host
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Host, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Host = value);
    }

    /// <summary>Gets or sets the host of the URL of the hyperlink, without the port.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname"/>
    [IdlMember("hostname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname")]
    public string Hostname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hostname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hostname = value);
    }

    /// <summary>Gets or sets the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port"/>
    [IdlMember("port", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port")]
    public string Port
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Port, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Port = value);
    }

    /// <summary>Gets or sets the path of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname"/>
    [IdlMember("pathname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname")]
    public string Pathname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Pathname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Pathname = value);
    }

    /// <summary>Gets or sets the query of the URL of the hyperlink, including the leading question mark.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search"/>
    [IdlMember("search", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search")]
    public string Search
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Search, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Search = value);
    }

    /// <summary>Gets or sets the fragment of the URL of the hyperlink, including the leading number sign.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash"/>
    [IdlMember("hash", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash")]
    public string Hash
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hash, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hash = value);
    }

    /// <summary>The stringifier of HTMLHyperlinkElementUtils: the href IDL attribute.</summary>
    [NotInIdl("The stringifier of the href IDL attribute")]
    public override string ToString() => Href;

    // The hyperlinks without an href attribute have no activation behavior (they are not the activation target of a click)
    internal override bool HasActivationBehavior => HasAttribute("href");

    internal override void ActivationBehavior(Event @event) => HyperlinkElementUtils.ActivationBehavior(this);
}

public partial class HtmlAnchorElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/text-level-semantics.html#dom-a-text</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/text-level-semantics.html#dom-a-text"/>
    [IdlMember("text", "https://html.spec.whatwg.org/multipage/text-level-semantics.html#dom-a-text")]
    public string Text
    {
        get => GetDescendantTextContent();
        set => StringReplaceAll(value ?? "");
    }
}

public partial class SvgAElement
{
    /// <summary>
    /// Gets the serialization of the origin of the URL of the hyperlink, or an empty string when there is no URL.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin")]
    public string Origin => HyperlinkElementUtils.Get(this, static url => url.Origin, "");

    /// <summary>Gets or sets the scheme of the URL of the hyperlink, followed by a colon.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol"/>
    [IdlMember("protocol", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol")]
    public string Protocol
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Protocol, ":");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Protocol = value);
    }

    /// <summary>Gets or sets the username of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username"/>
    [IdlMember("username", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username")]
    public string Username
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Username, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Username = value);
    }

    /// <summary>Gets or sets the password of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password"/>
    [IdlMember("password", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password")]
    public string Password
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Password, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Password = value);
    }

    /// <summary>Gets or sets the host and the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host"/>
    [IdlMember("host", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host")]
    public string Host
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Host, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Host = value);
    }

    /// <summary>Gets or sets the host of the URL of the hyperlink, without the port.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname"/>
    [IdlMember("hostname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname")]
    public string Hostname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hostname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hostname = value);
    }

    /// <summary>Gets or sets the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port"/>
    [IdlMember("port", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port")]
    public string Port
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Port, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Port = value);
    }

    /// <summary>Gets or sets the path of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname"/>
    [IdlMember("pathname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname")]
    public string Pathname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Pathname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Pathname = value);
    }

    /// <summary>Gets or sets the query of the URL of the hyperlink, including the leading question mark.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search"/>
    [IdlMember("search", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search")]
    public string Search
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Search, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Search = value);
    }

    /// <summary>Gets or sets the fragment of the URL of the hyperlink, including the leading number sign.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash"/>
    [IdlMember("hash", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash")]
    public string Hash
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hash, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hash = value);
    }
}

public partial class MathMLAnchorElement
{
    // https://w3c.github.io/mathml-core/#the-a-element: the attributes have the same semantics as for the HTML a element,
    // which is a hyperlink when it has an href attribute (https://html.spec.whatwg.org/multipage/links.html#links-created-by-a-and-area-elements)
    internal override bool HasActivationBehavior => HasAttribute("href");

    internal override void ActivationBehavior(Event @event) => HyperlinkElementUtils.ActivationBehavior(this);

    /// <summary>
    /// Gets the serialization of the origin of the URL of the hyperlink, or an empty string when there is no URL.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin"/>
    [IdlMember("origin", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-origin")]
    public string Origin => HyperlinkElementUtils.Get(this, static url => url.Origin, "");

    /// <summary>Gets or sets the scheme of the URL of the hyperlink, followed by a colon.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol"/>
    [IdlMember("protocol", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-protocol")]
    public string Protocol
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Protocol, ":");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Protocol = value);
    }

    /// <summary>Gets or sets the username of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username"/>
    [IdlMember("username", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-username")]
    public string Username
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Username, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Username = value);
    }

    /// <summary>Gets or sets the password of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password"/>
    [IdlMember("password", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-password")]
    public string Password
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Password, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Password = value);
    }

    /// <summary>Gets or sets the host and the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host"/>
    [IdlMember("host", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-host")]
    public string Host
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Host, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Host = value);
    }

    /// <summary>Gets or sets the host of the URL of the hyperlink, without the port.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname"/>
    [IdlMember("hostname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hostname")]
    public string Hostname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hostname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hostname = value);
    }

    /// <summary>Gets or sets the port of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port"/>
    [IdlMember("port", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-port")]
    public string Port
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Port, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Port = value);
    }

    /// <summary>Gets or sets the path of the URL of the hyperlink.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname"/>
    [IdlMember("pathname", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-pathname")]
    public string Pathname
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Pathname, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Pathname = value);
    }

    /// <summary>Gets or sets the query of the URL of the hyperlink, including the leading question mark.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search"/>
    [IdlMember("search", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-search")]
    public string Search
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Search, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Search = value);
    }

    /// <summary>Gets or sets the fragment of the URL of the hyperlink, including the leading number sign.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash"/>
    [IdlMember("hash", "https://html.spec.whatwg.org/multipage/links.html#dom-hyperlink-hash")]
    public string Hash
    {
        get => HyperlinkElementUtils.Get(this, static url => url.Hash, "");
        set => HyperlinkElementUtils.Set(this, value, static (url, value) => url.Hash = value);
    }
}

public partial class SvgAElement
{
    // https://svgwg.org/svg2-draft/linking.html#AElement: the element is a hyperlink when it has an href (or xlink:href)
    // attribute, followed when it is activated (https://svgwg.org/svg2-draft/linking.html#processingIRI)
    internal override bool HasActivationBehavior => HasAttribute("href") || HasAttributeNS("http://www.w3.org/1999/xlink", "href");

    internal override void ActivationBehavior(Event @event) => HyperlinkElementUtils.ActivationBehavior(this);

    /// <summary>https://svgwg.org/svg2-draft/linking.html#__svg__SVGAElement__referrerPolicy</summary>
    /// <seealso href="https://w3c.github.io/svgwg/svg2-draft/linking.html#__svg__SVGAElement__referrerPolicy"/>
    [IdlMember("referrerPolicy", "https://w3c.github.io/svgwg/svg2-draft/linking.html#__svg__SVGAElement__referrerPolicy")]
    public string ReferrerPolicy
    {
        get => Reflection.GetEnumerated(this, "referrerpolicy", EnumeratedAttributes.Referrerpolicy);
        set => Reflection.SetString(this, "referrerpolicy", value);
    }
}
