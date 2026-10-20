namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/obsolete.html#external: the obsolete <c>window.external</c> object, whose methods
/// do nothing.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#external"/>
[IdlInterface("External", "https://html.spec.whatwg.org/multipage/obsolete.html#external")]
public sealed class External
{
    internal External()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-addsearchprovider: does nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-addsearchprovider"/>
    [IdlMember("AddSearchProvider", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-addsearchprovider")]
    public void AddSearchProvider()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-issearchproviderinstalled: does nothing.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-issearchproviderinstalled"/>
    [IdlMember("IsSearchProviderInstalled", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-issearchproviderinstalled")]
    public void IsSearchProviderInstalled()
    {
    }
}
