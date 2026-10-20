#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA2234 // Pass system uri objects instead of strings
namespace Meziantou.Framework.Http.HstsSample;

/// <summary>The examples of src/Meziantou.Framework.Http.Hsts/readme.md. They send real requests, so they are compiled but not run.</summary>
public static class ReadmeExamples
{
    public static async Task UpgradePreloadedHost()
    {
        #region HstsClientHandler_UpgradePreloadedHost
        var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
        using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);

        // Automatically upgrade to HTTPS as github.com is in the HSTS preload list
        using var response = await client.GetAsync("http://github.com");
        #endregion
    }

    public static async Task Redirects(HttpClient client)
    {
        #region HstsClientHandler_Redirects
        // http://example.com/go redirects to http://github.com, which is in the preload list.
        // The redirect is followed as https://github.com
        using var response = await client.GetAsync("http://example.com/go");
        #endregion
    }

    public static void MaxAutomaticRedirections(HstsDomainPolicyCollection policies)
    {
        #region HstsClientHandler_MaxAutomaticRedirections
        var inner = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(new HstsClientHandler(inner, policies, maxAutomaticRedirections: 50), disposeHandler: true);
        #endregion
    }

    public static void NoRedirects(HstsDomainPolicyCollection policies)
    {
        #region HstsClientHandler_NoRedirects
        // HstsClientHandler does not follow redirects either: the 3xx response is returned as is
        var inner = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(new HstsClientHandler(inner, policies), disposeHandler: true);
        #endregion
    }
}
