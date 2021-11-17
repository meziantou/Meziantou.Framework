using System;
using System.Net.Http;
using System.Threading.Tasks;
using Meziantou.AspNetCore.Components;
using Meziantou.AspNetCore.Components.WebAssembly;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorAppSample
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddClipboard();
            builder.Services.AddTimeZoneServices();
            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });


            builder.Services.AddHttpClient<WeatherForecastClient>()
                .ConfigureHttpClient(client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
                .AddHttpMessageHandler(() => new DefaultBrowserOptionsMessageHandler() { DefaultBrowserRequestCache = BrowserRequestCache.NoCache });


            await builder.Build().RunAsync().ConfigureAwait(false);
        }
    }
}
