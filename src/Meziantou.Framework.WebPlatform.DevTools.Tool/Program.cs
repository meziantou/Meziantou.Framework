using System.CommandLine;
using System.Globalization;
using System.Net;
using Meziantou.Framework.WebPlatform.DevTools;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.DevToolsTool;

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
            DefaultValueFactory = _ => 9222,
        };
        var allowNetworkOption = new Option<bool>("--allow-network")
        {
            Description = "Let the pages load the documents and the resources of any URL (without it, the pages can only use the content they are given and the intercepted requests)",
        };
        var systemFontsOption = new Option<bool>("--system-fonts")
        {
            Description = "Let the pages use the fonts installed on the machine",
        };
        var maxConnectionsOption = new Option<int?>("--max-connections")
        {
            Description = "Maximum number of clients connected at the same time (the others wait)",
        };

        var rootCommand = new RootCommand("Run a Chrome DevTools Protocol server whose browser is Meziantou.Framework.WebPlatform (as a headless Chromium " + DevToolsServer.ChromiumVersion + ")")
        {
            hostOption,
            portOption,
            allowNetworkOption,
            systemFontsOption,
            maxConnectionsOption,
        };

        rootCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var host = parseResult.GetRequiredValue(hostOption);
            if (!IPAddress.TryParse(host, out var address))
            {
                await Console.Error.WriteLineAsync(("Invalid address: " + host).AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            if (parseResult.GetValue(maxConnectionsOption) is <= 0)
            {
                await Console.Error.WriteLineAsync("The maximum number of connections must be positive".AsMemory(), cancellationToken).ConfigureAwait(false);
                return 1;
            }

            var options = new DevToolsServerOptions
            {
                BindAddress = address,
                Port = parseResult.GetValue(portOption),
                DocumentLoader = parseResult.GetValue(allowNetworkOption) ? DocumentLoader.Create() : null,
                UseSystemFonts = parseResult.GetValue(systemFontsOption),
                MaxConnections = parseResult.GetValue(maxConnectionsOption),
            };

            await using var server = new DevToolsServer(options);
            await server.StartAsync(cancellationToken).ConfigureAwait(false);

            // The line Chromium prints when it starts with --remote-debugging-port
            Console.WriteLine("DevTools listening on " + server.WebSocketEndpoint);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"HTTP endpoint: {server.HttpEndpoint} (ConnectOverCDPAsync, /json/version)"));
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
