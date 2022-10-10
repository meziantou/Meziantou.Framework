namespace Meziantou.Framework.NuGetPackageValidation.Internal;
internal static class ShareHttpClient
{
    public static HttpClient Instance { get; } = CreateHttpClient();

    public static async Task<bool> IsUrlAccessible(this HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var socketHandler = new SocketsHttpHandler()
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };
        return new HttpClient(socketHandler, disposeHandler: true);
    }
}
