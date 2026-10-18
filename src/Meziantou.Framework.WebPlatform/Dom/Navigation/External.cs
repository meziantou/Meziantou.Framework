namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// https://html.spec.whatwg.org/multipage/obsolete.html#external: the obsolete <c>window.external</c> object, whose methods
/// do nothing.
/// </summary>
[IdlInterface("External", "https://html.spec.whatwg.org/multipage/obsolete.html#external")]
public sealed class External
{
    internal External()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-addsearchprovider: does nothing.</summary>
    [IdlMember("AddSearchProvider", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-addsearchprovider")]
    public void AddSearchProvider()
    {
    }

    /// <summary>https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-issearchproviderinstalled: does nothing.</summary>
    [IdlMember("IsSearchProviderInstalled", "https://html.spec.whatwg.org/multipage/obsolete.html#dom-external-issearchproviderinstalled")]
    public void IsSearchProviderInstalled()
    {
    }
}
