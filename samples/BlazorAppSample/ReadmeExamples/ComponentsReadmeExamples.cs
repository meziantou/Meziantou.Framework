using Meziantou.AspNetCore.Components;
using Meziantou.AspNetCore.Components.WebAssembly;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace BlazorAppSample.ReadmeExamples;

/// <summary>
/// The C# examples of the readme files of Meziantou.AspNetCore.Components and Meziantou.AspNetCore.Components.WebAssembly.
/// The Razor examples are the other files of this folder.
/// </summary>
public static class ComponentsReadmeExamples
{
    public static void RegisterServices(WebAssemblyHostBuilder builder)
    {
        #region Components_RegisterServices
        builder.Services.AddClipboard();
        builder.Services.AddQueryStringParameters();
        builder.Services.AddTimeZoneServices();
        #endregion
    }

    public static async Task DefaultBrowserOptions(string[] args)
    {
        #region DefaultBrowserOptionsMessageHandler_Usage
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        // Add HttpClient with default browser options
        builder.Services.AddHttpClient<MyApiClient>()
            .ConfigureHttpClient(client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
            .AddHttpMessageHandler(() => new DefaultBrowserOptionsMessageHandler()
            {
                DefaultBrowserRequestCache = BrowserRequestCache.NoCache
            });

        await builder.Build().RunAsync();
        #endregion
    }

    private sealed class MyApiClient(HttpClient httpClient)
    {
        public Task<string> GetAsync() => httpClient.GetStringAsync("api/data");
    }
}
