using System.CommandLine;
using System.Net;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright;

namespace Meziantou.Framework.WebPlatform.PlaywrightTool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var hostOption = new Option<string>("--host")
        {
            Description = "Address to listen on (use 0.0.0.0 to accept the remote clients)",
            DefaultValueFactory = _ => "127.0.0.1",
        };
        var portOption = new Option<int>("--port")
        {
            Description = "Port to listen on (0 chooses a free port)",
            DefaultValueFactory = _ => 0,
        };
        var pathOption = new Option<string>("--path")
        {
            Description = "Path of the WebSocket endpoint",
            DefaultValueFactory = _ => "/",
        };
        var allowNetworkOption = new Option<bool>("--allow-network")
        {
            Description = "Let the pages load the documents and the resources of any URL (without it, the pages can only use page.setContent() and the routes)",
        };
        var systemFontsOption = new Option<bool>("--system-fonts")
        {
            Description = "Let the pages use the fonts installed on the machine",
        };
        var browserNameOption = new Option<string>("--browser-name")
        {
            Description = "Name of the browser reported to the clients (chromium, firefox or webkit)",
            DefaultValueFactory = _ => "chromium",
        };
        var anyClientVersionOption = new Option<bool>("--allow-any-client-version")
        {
            Description = "Accept the clients of another version of Playwright (the protocol may differ)",
        };

        var rootCommand = new RootCommand($"Run a Playwright {PlaywrightServer.PlaywrightVersion} server whose browser is Meziantou.Framework.WebPlatform")
        {
            hostOption,
            portOption,
            pathOption,
            allowNetworkOption,
            systemFontsOption,
            browserNameOption,
            anyClientVersionOption,
        };

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var host = parseResult.GetRequiredValue(hostOption);
            if (!IPAddress.TryParse(host, out var address))
            {
                await Console.Error.WriteLineAsync(("Invalid address: " + host).AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            var options = new PlaywrightServerOptions
            {
                BindAddress = address,
                Port = parseResult.GetValue(portOption),
                Path = parseResult.GetRequiredValue(pathOption),
                DocumentLoader = parseResult.GetValue(allowNetworkOption) ? DocumentLoader.Create() : null,
                UseSystemFonts = parseResult.GetValue(systemFontsOption),
                BrowserName = parseResult.GetRequiredValue(browserNameOption),
                AllowAnyClientVersion = parseResult.GetValue(anyClientVersionOption),
            };

            await using var server = new PlaywrightServer(options);
            await server.StartAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Listening on " + server.WsEndpoint);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return 0;
        });

        return rootCommand.Parse(args).InvokeAsync();
    }
}
