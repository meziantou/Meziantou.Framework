using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Meziantou.Framework;

public sealed class HttpClientMock : IAsyncDisposable
{
    private bool _running;

    public HttpClientMock()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        Application = builder.Build();
    }

    public WebApplication Application { get; }

    public HttpClient CreateHttpClient()
    {
        StartServer();
        return Application.GetTestClient();
    }

    public HttpMessageHandler CreateHttpMessageHandler()
    {
        StartServer();
        return Application.GetTestServer().CreateHandler();
    }

    private void StartServer()
    {
        if (!_running)
        {
            _running = true;
            _ = Application.RunAsync();
        }
    }

    public ValueTask DisposeAsync() => Application.DisposeAsync();
}
