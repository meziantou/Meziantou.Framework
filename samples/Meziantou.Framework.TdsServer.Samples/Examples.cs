using System.Net;
using System.Net.Security;
using System.Security.Claims;
using Meziantou.Framework.Tds;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace TdsServerSamples;

// The examples of docs/TdsServer that need a fixed port, certificate files or a long-running host. CI compiles
// them but does not run them; the examples that can run are in tests/Meziantou.Framework.TdsServer.Tests.
public static class Examples
{
    public static void ServerOptions(WebApplicationBuilder builder)
    {
        #region TdsServer_ServerOptions
        builder.AddTdsServer(options =>
        {
            options.AddTcpListener(port: 1433, bindAddress: IPAddress.Any); // every IPv4 interface
            options.AddTcpListener(port: 1433, bindAddress: IPAddress.IPv6Any);
            options.PacketSize = 8192; // size of the packets the server writes
            options.MaxMessageSize = 64 * 1024 * 1024; // largest request a client can send
            options.TlsPfxPath = "certificates/server.pfx";
            options.TlsPfxPassword = builder.Configuration["Tds:CertificatePassword"];
        });
        #endregion
    }

    public static void ServeHttpToo(string[] args)
    {
        #region TdsServer_ServeHttpToo
        var builder = WebApplication.CreateBuilder(args);
        builder.AddTdsServer(options => options.AddTcpListener(port: 14330));

        // AddTdsServer declares Kestrel endpoints in code, so Kestrel no longer binds the default HTTP URLs
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(5000));

        var app = builder.Build();
        app.MapGet("/health", () => "OK");
        app.MapTdsHandlers(
            authenticate: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            query: (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));
        app.Run();
        #endregion
    }

    public static void RequireEncryption(WebApplicationBuilder builder)
    {
        #region TdsServer_RequireEncryption
        builder.AddTdsServer(options =>
        {
            options.AddTcpListener(port: 1433, bindAddress: IPAddress.Any);
            options.TlsPemCertificatePath = "certificates/server.crt.pem";
            options.TlsPemPrivateKeyPath = "certificates/server.key.pem";
            options.RequireEncryption = true; // reject clients that do not encrypt
        });
        #endregion
    }

    public static async Task StandaloneWithLogging()
    {
        #region TdsServer_StandaloneWithLogging
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Debug));

        var options = new TdsServerOptions();
        options.AddTcpListener(port: 14330);

        using var server = new TdsServer(
            options,
            authenticationHandler: (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            queryHandler: (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()),
            logger: loggerFactory.CreateLogger<TdsServer>());
        await server.StartAsync();

        Console.WriteLine("Listening on port " + server.Ports[0] + ". Press Enter to stop.");
        Console.ReadLine();
        #endregion
    }

    public static TdsAuthenticationDelegate IntegratedSecurity()
    {
        #region TdsServer_IntegratedSecurity
        TdsAuthenticationDelegate authenticate = (context, cancellationToken) =>
        {
            if (!context.IsIntegratedSecurity)
            {
                return ValueTask.FromResult(TdsAuthenticationResult.Fail("Use Integrated Security=true."));
            }

            // Every round of the exchange gets the same context: keep the state of the exchange in Items. The server
            // disposes the NegotiateAuthentication instance once the login completes.
            if (!context.Items.TryGetValue(typeof(NegotiateAuthentication), out var value) || value is not NegotiateAuthentication negotiate)
            {
                negotiate = new NegotiateAuthentication(new NegotiateAuthenticationServerOptions());
                context.Items[typeof(NegotiateAuthentication)] = negotiate;
            }

            var outgoingToken = negotiate.GetOutgoingBlob(context.SspiToken.Span, out var status);
            var result = status switch
            {
                NegotiateAuthenticationStatusCode.ContinueNeeded => TdsAuthenticationResult.ContinueSspi(outgoingToken),
                NegotiateAuthenticationStatusCode.Completed => TdsAuthenticationResult.Success(outgoingToken, userContext: new ClaimsPrincipal(negotiate.RemoteIdentity)),
                _ => TdsAuthenticationResult.Fail("Login failed."),
            };

            return ValueTask.FromResult(result);
        };
        #endregion

        return authenticate;
    }
}
