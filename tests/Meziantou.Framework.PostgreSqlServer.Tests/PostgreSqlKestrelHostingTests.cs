using System.Net;
using Meziantou.Framework.PostgreSql.Handler;
using Meziantou.Framework.PostgreSql.Hosting;
using Meziantou.Xunit;
using Microsoft.AspNetCore.Builder;
using Npgsql;

namespace Meziantou.Framework.PostgreSql.Tests;

/// <summary>
/// The Kestrel host is not interchangeable with the standalone one: it hands the processor two distinct
/// pipe-backed streams rather than a single NetworkStream, which is what the DuplexStream adapter exists for.
/// </summary>
[RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
public sealed class PostgreSqlKestrelHostingTests
{
    private const string Password = "Password123!";

    private static string ConnectionString(int port, string sslMode = "Disable")
        => $"Host={IPAddress.Loopback};Port={port};Username=app;Password={Password};Database=postgres;SSL Mode={sslMode};Trust Server Certificate=true;Pooling=false;Timeout=30;Command Timeout=30;Server Compatibility Mode=NoTypeLoading";

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static PostgreSqlQueryResult IntResult(int value)
    {
        var resultSet = new PostgreSqlResultSet();
        resultSet.Columns.Add(new PostgreSqlColumn("n", PostgreSqlColumnType.Int32));
        resultSet.Rows.Add([value]);
        var result = new PostgreSqlQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    }

    private static async Task<WebApplication> StartHostAsync(int port, string? pfxPath = null, string? pfxPassword = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        _ = builder.AddPostgreSqlServer(options =>
        {
            options.AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword;
            _ = options.AddTcpListener(port, IPAddress.Loopback);
            if (pfxPath is not null)
            {
                options.TlsPfxPath = pfxPath;
                options.TlsPfxPassword = pfxPassword;
            }
        });

        var app = builder.Build();
        _ = app.MapPostgreSqlAuthenticationHandler((context, _) => ValueTask.FromResult(context.ValidatePassword(Password)
            ? PostgreSqlAuthenticationResult.Success()
            : PostgreSqlAuthenticationResult.Fail("invalid password")));
        _ = app.MapPostgreSqlQueryHandler((_, _) => ValueTask.FromResult(IntResult(99)));

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task QueriesWorkOverTheKestrelTransport()
    {
        var port = GetFreePort();
        await using var app = await StartHostAsync(port);

        await using var connection = new NpgsqlConnection(ConnectionString(port));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT n";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(99, reader.GetInt32(0));
    }

    [Fact]
    public async Task WrongPasswordIsRejectedOverTheKestrelTransport()
    {
        var port = GetFreePort();
        await using var app = await StartHostAsync(port);

        await using var connection = new NpgsqlConnection(ConnectionString(port).Replace(Password, "wrong", StringComparison.Ordinal));
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await connection.OpenAsync());
        Assert.Equal("28P01", exception.SqlState);
    }

    [Fact]
    public async Task TlsWorksOverTheKestrelTransport()
    {
        // Exercises DuplexStream, which only the split-pipe transport uses.
        using var certificate = TestCertificate.Create();
        var port = GetFreePort();
        await using var app = await StartHostAsync(port, certificate.PfxPath, certificate.PfxPassword);

        await using var connection = new NpgsqlConnection(ConnectionString(port, sslMode: "Require"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT n";
        Assert.Equal(99, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task WithoutAQueryHandlerTheServerReportsAnError()
    {
        var port = GetFreePort();
        var builder = WebApplication.CreateSlimBuilder();
        _ = builder.AddPostgreSqlServer(options =>
        {
            options.AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword;
            _ = options.AddTcpListener(port, IPAddress.Loopback);
        });

        await using var app = builder.Build();
        _ = app.MapPostgreSqlAuthenticationHandler((_, _) => ValueTask.FromResult(PostgreSqlAuthenticationResult.Success()));
        await app.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(port));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT n";
        _ = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
    }
}
