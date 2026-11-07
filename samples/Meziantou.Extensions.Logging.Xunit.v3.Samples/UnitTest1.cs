#pragma warning disable IDE1006 // Naming Styles: the namespace contains "v3"
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meziantou.Extensions.Logging.Xunit.v3.Samples;

#region XUnitLogger_WebApplicationFactory
public class UnitTest1(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task Test1()
    {
        using var application = new WebApplicationFactory<Program>();
        using var factory = application.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(builder =>
            {
                // You can override the logging configuration if needed
                //builder.SetMinimumLevel(LogLevel.Trace);
                //builder.AddFilter(_ => true);

                // Register the xUnit logger provider
                builder.AddXunit(testOutputHelper, new XUnitLoggerOptions { IncludeScopes = true });
            });
        });

        using var client = factory.CreateClient();
        _ = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
    }
}
#endregion
