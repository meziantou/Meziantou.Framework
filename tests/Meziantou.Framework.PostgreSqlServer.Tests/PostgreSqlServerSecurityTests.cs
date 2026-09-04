using System.Net;
using Meziantou.Framework.PostgreSql.Handler;
using Meziantou.Xunit;
using Npgsql;

namespace Meziantou.Framework.PostgreSql.Tests;

[RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
public sealed class PostgreSqlServerSecurityTests
{
    private const string Password = "Password123!";

    private static PostgreSqlServer CreateServer(
        PostgreSqlAuthenticationMethod method = PostgreSqlAuthenticationMethod.ClearTextPassword,
        Action<PostgreSqlServerOptions>? configure = null,
        PostgreSqlAuthenticationDelegate? authenticationHandler = null)
    {
        var options = new PostgreSqlServerOptions { AuthenticationMethod = method };
        options.AddTcpListener(0, IPAddress.Loopback);
        configure?.Invoke(options);

        return new PostgreSqlServer(
            options,
            authenticationHandler ?? ((context, _) => ValueTask.FromResult(context.ValidatePassword(Password)
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password"))),
            (context, _) => ValueTask.FromResult(CreateIntResult()));
    }

    private static PostgreSqlQueryResult CreateIntResult(int value = 1)
    {
        var resultSet = new PostgreSqlResultSet();
        resultSet.Columns.Add(new PostgreSqlColumn("value", PostgreSqlColumnType.Int32));
        resultSet.Rows.Add([value]);
        var result = new PostgreSqlQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    }

    private static string ConnectionString(int port, string password = Password, string sslMode = "Disable")
        => $"Host={IPAddress.Loopback};Port={port};Username=app;Password={password};Database=postgres;SSL Mode={sslMode};Trust Server Certificate=true;Pooling=false;Timeout=30;Command Timeout=30;Server Compatibility Mode=NoTypeLoading";

    [Theory]
    [InlineData(PostgreSqlAuthenticationMethod.ClearTextPassword)]
    [InlineData(PostgreSqlAuthenticationMethod.Md5Password)]
    [InlineData(PostgreSqlAuthenticationMethod.ScramSha256)]
    public async Task WrongPassword_IsRejected(PostgreSqlAuthenticationMethod method)
    {
        using var server = CreateServer(method);
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0], password: "wrong-password"));
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await connection.OpenAsync());
        Assert.Equal("28P01", exception.SqlState);
    }

    [Theory]
    [InlineData(PostgreSqlAuthenticationMethod.ClearTextPassword)]
    [InlineData(PostgreSqlAuthenticationMethod.Md5Password)]
    [InlineData(PostgreSqlAuthenticationMethod.ScramSha256)]
    public async Task CorrectPassword_IsAccepted(PostgreSqlAuthenticationMethod method)
    {
        using var server = CreateServer(method);
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task Md5Authentication_ProvidesSaltAndResponse()
    {
        PostgreSqlAuthenticationContext? captured = null;
        using var server = CreateServer(
            PostgreSqlAuthenticationMethod.Md5Password,
            authenticationHandler: (context, _) =>
            {
                captured = context;
                return ValueTask.FromResult(context.ValidatePassword(Password)
                    ? PostgreSqlAuthenticationResult.Success()
                    : PostgreSqlAuthenticationResult.Fail("invalid password"));
            });
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();

        Assert.NotNull(captured);
        Assert.Equal(PostgreSqlAuthenticationMethod.Md5Password, captured.Method);
        Assert.Equal("app", captured.UserName);
        Assert.Equal("postgres", captured.Database);
        Assert.NotNull(captured.Md5PasswordResponse);
        Assert.StartsWith("md5", captured.Md5PasswordResponse);
    }

    [Fact]
    public async Task ScramSuccessWithoutValidatePassword_IsReportedAsAHandlerError()
    {
        // ValidatePassword is what computes the SCRAM server signature, so returning Success() without
        // calling it cannot produce a valid exchange. The client must not be told its credentials were wrong.
        using var server = CreateServer(
            PostgreSqlAuthenticationMethod.ScramSha256,
            authenticationHandler: (_, _) => ValueTask.FromResult(PostgreSqlAuthenticationResult.Success()));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await connection.OpenAsync());
        Assert.Equal("XX000", exception.SqlState);
        Assert.Contains("ValidatePassword", exception.MessageText);
    }

    [Fact]
    public async Task AuthenticationHandlerThatThrows_ReportsAnErrorInsteadOfDroppingTheConnection()
    {
        using var server = CreateServer(
            PostgreSqlAuthenticationMethod.ClearTextPassword,
            authenticationHandler: (_, _) => throw new InvalidOperationException("boom"));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await connection.OpenAsync());
        Assert.Equal("XX000", exception.SqlState);
    }

    [Fact]
    public async Task StartupPacketLargerThanTheLimit_IsRejectedWithoutAllocatingIt()
    {
        using var server = CreateServer();
        await server.StartAsync();
        var port = server.Ports[0];

        using (var client = await RawPostgreSqlClient.ConnectAsync(port))
        {
            // 1 GiB declared, no body sent. Before the limit this allocated the full buffer up front.
            await client.SendRawStartupAsync(1024 * 1024 * 1024, ReadOnlyMemory<byte>.Empty);
            Assert.Null(await client.ReadMessageAsync());
        }

        await AssertServerStillWorksAsync(port);
    }

    [Fact]
    public async Task MessageLargerThanTheLimit_IsRejectedWithoutAllocatingIt()
    {
        using var server = CreateServer(configure: options => options.MaxMessageSize = 8 * 1024);
        await server.StartAsync();
        var port = server.Ports[0];

        using (var client = await RawPostgreSqlClient.ConnectAsync(port))
        {
            await client.AuthenticateClearTextAsync();
            await client.SendRawMessageAsync((byte)'Q', int.MaxValue - 8, ReadOnlyMemory<byte>.Empty);
            var messages = await client.ReadUntilReadyForQueryAsync();
            Assert.All(messages, message => Assert.NotEqual((byte)'D', message.Type));
        }

        await AssertServerStillWorksAsync(port);
    }

    [Fact]
    public async Task DuplicateSslRequest_IsRejected()
    {
        // Without this the negotiation loop let a client stack SslStreams on a single connection.
        using var server = CreateServer();
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.SendSslRequestAsync();
        Assert.Equal((byte)'N', await client.ReadSingleByteAsync());

        await client.SendSslRequestAsync();
        var message = await client.ReadMessageAsync();
        Assert.NotNull(message);
        Assert.Equal((byte)'E', message.Type);
        Assert.Equal("08P01", message.ErrorFields()['C']);
    }

    [Fact]
    public async Task UnknownMessageType_IsReportedAndTheConnectionSurvivesAfterSync()
    {
        using var server = CreateServer();
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();

        await client.SendMessageAsync((byte)'!', ReadOnlyMemory<byte>.Empty);
        await client.SendSyncAsync();
        var messages = await client.ReadUntilReadyForQueryAsync();
        Assert.Equal((byte)'E', messages[0].Type);
        Assert.Equal((byte)'Z', messages[^1].Type);

        // The same connection must still be usable.
        await client.SendSimpleQueryAsync("SELECT 1");
        var afterRecovery = await client.ReadUntilReadyForQueryAsync();
        Assert.Contains(afterRecovery, message => message.Type == (byte)'D');
    }

    [Fact]
    public async Task BindToAnUnknownStatement_ReportsAnErrorAndSkipsUntilSync()
    {
        using var server = CreateServer();
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();

        await client.SendBindAsync(portalName: "", statementName: "does-not-exist");

        // Messages between the error and Sync must be discarded rather than answered.
        await client.SendMessageAsync((byte)'E', ExecutePayload());
        await client.SendSyncAsync();

        var messages = await client.ReadUntilReadyForQueryAsync();
        Assert.Equal((byte)'E', messages[0].Type);
        Assert.Equal("08P01", messages[0].ErrorFields()['C']);
        Assert.Equal([(byte)'E', (byte)'Z'], messages.Select(message => message.Type));

        await client.SendSimpleQueryAsync("SELECT 1");
        var afterRecovery = await client.ReadUntilReadyForQueryAsync();
        Assert.Contains(afterRecovery, message => message.Type == (byte)'D');
    }

    [Fact]
    public async Task PreparedStatementLimit_IsEnforced()
    {
        using var server = CreateServer(configure: options => options.MaxPreparedStatementsPerConnection = 4);
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();

        for (var i = 0; i < 10; i++)
        {
            await client.SendParseAsync($"statement-{i}", "SELECT 1");
        }

        await client.SendSyncAsync();
        var messages = await client.ReadUntilReadyForQueryAsync();
        Assert.Contains(messages, message => message.Type == (byte)'E');
    }

    [Fact]
    public async Task RequireEncryptionWithoutTls_RejectsTheConnection()
    {
        using var certificate = TestCertificate.Create();
        using var server = CreateServer(configure: options =>
        {
            options.RequireEncryption = true;
            options.TlsPfxPath = certificate.PfxPath;
            options.TlsPfxPassword = certificate.PfxPassword;
        });
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0], sslMode: "Disable"));
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await connection.OpenAsync());
        Assert.Equal("28000", exception.SqlState);
    }

    [Fact]
    public async Task RequireEncryption_AlsoAppliesToCancelRequests()
    {
        // The cancel path used to return before the encryption check, so it accepted plaintext.
        using var certificate = TestCertificate.Create();
        using var server = CreateServer(configure: options =>
        {
            options.RequireEncryption = true;
            options.TlsPfxPath = certificate.PfxPath;
            options.TlsPfxPassword = certificate.PfxPassword;
        });
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.SendCancelRequestAsync(processId: 1, secretKey: 2);
        var message = await client.ReadMessageAsync();
        Assert.NotNull(message);
        Assert.Equal((byte)'E', message.Type);
        Assert.Equal("28000", message.ErrorFields()['C']);
    }

    [Fact]
    public async Task SslRequestWithoutACertificate_FallsBackToPlaintext()
    {
        // The default configuration has no certificate, and Npgsql's default SSL mode asks for TLS first.
        using var server = CreateServer();
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0], sslMode: "Prefer"));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ConcurrentConnectionLimit_IsEnforced()
    {
        using var server = CreateServer(configure: options => options.MaxConcurrentConnections = 1);
        await server.StartAsync();
        var port = server.Ports[0];

        await using var first = new NpgsqlConnection(ConnectionString(port));
        await first.OpenAsync();

        await using var second = new NpgsqlConnection(ConnectionString(port));
        _ = await Assert.ThrowsAnyAsync<Exception>(async () => await second.OpenAsync());
    }

    private static async Task AssertServerStillWorksAsync(int port)
    {
        await using var connection = new NpgsqlConnection(ConnectionString(port));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    private static byte[] ExecutePayload()
    {
        // Portal name (empty) followed by an unlimited row count.
        return [0, 0, 0, 0, 0];
    }
}
