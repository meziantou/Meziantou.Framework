#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA2234 // Pass system uri objects instead of strings
namespace Meziantou.Framework.Http.HstsSample;

public static class Examples
{
    public static async Task UpgradeToHttps()
    {
        #region HstsClientHandler_Example
        var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
        using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);

        // Automatically upgrade to HTTPS as github.com is in the HSTS preload list
        using var response = await client.GetAsync("http://github.com");
        #endregion
    }
}
