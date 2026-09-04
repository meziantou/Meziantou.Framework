using System.Net;
using Meziantou.Framework.PostgreSql.Handler;
using Meziantou.Xunit;
using Npgsql;

namespace Meziantou.Framework.PostgreSql.Tests;

[RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
public sealed class PostgreSqlServerBehaviorTests
{
    private const string Password = "Password123!";

    private static PostgreSqlServer CreateServer(PostgreSqlQueryDelegate queryHandler, Action<PostgreSqlServerOptions>? configure = null)
    {
        var options = new PostgreSqlServerOptions { AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword };
        options.AddTcpListener(0, IPAddress.Loopback);
        configure?.Invoke(options);

        return new PostgreSqlServer(
            options,
            (context, _) => ValueTask.FromResult(context.ValidatePassword(Password)
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password")),
            queryHandler);
    }

    private static string ConnectionString(int port)
        => $"Host={IPAddress.Loopback};Port={port};Username=app;Password={Password};Database=postgres;SSL Mode=Disable;Pooling=false;Timeout=30;Command Timeout=30;Server Compatibility Mode=NoTypeLoading";

    /// <summary>Answers Describe and execution with the same shape, which is what the protocol requires.</summary>
    private static PostgreSqlQueryDelegate StaticResult(Func<PostgreSqlQueryResult> factory)
        => (_, _) => ValueTask.FromResult(factory());

    private static PostgreSqlQueryResult Rows(PostgreSqlColumnType type, string columnName, params object?[] values)
    {
        var resultSet = new PostgreSqlResultSet();
        resultSet.Columns.Add(new PostgreSqlColumn(columnName, type));
        foreach (var value in values)
        {
            resultSet.Rows.Add([value]);
        }

        var result = new PostgreSqlQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    }

    [Fact]
    public async Task DeclaredColumnTypesReachTheClient()
    {
        // Without AllResultTypesAreUnknown the client decodes by the OID the server advertised, so this is
        // what proves the handler's declared types are actually honoured.
        using var server = CreateServer(StaticResult(() =>
        {
            var resultSet = new PostgreSqlResultSet();
            resultSet.Columns.Add(new PostgreSqlColumn("n", PostgreSqlColumnType.Int32));
            resultSet.Columns.Add(new PostgreSqlColumn("b", PostgreSqlColumnType.Boolean));
            resultSet.Columns.Add(new PostgreSqlColumn("l", PostgreSqlColumnType.Int64));
            resultSet.Columns.Add(new PostgreSqlColumn("d", PostgreSqlColumnType.Double));
            resultSet.Columns.Add(new PostgreSqlColumn("s", PostgreSqlColumnType.Text));
            resultSet.Columns.Add(new PostgreSqlColumn("g", PostgreSqlColumnType.Uuid));
            resultSet.Columns.Add(new PostgreSqlColumn("m", PostgreSqlColumnType.Numeric));
            resultSet.Rows.Add([42, true, 9_000_000_000L, 1.5d, "hello", Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2"), 123.45m]);

            var result = new PostgreSqlQueryResult();
            result.ResultSets.Add(resultSet);
            return result;
        }));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT anything";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(42, reader.GetInt32(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal(9_000_000_000L, reader.GetInt64(2));
        Assert.Equal(1.5d, reader.GetDouble(3));
        Assert.Equal("hello", reader.GetString(4));
        Assert.Equal(Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2"), reader.GetGuid(5));
        Assert.Equal(123.45m, reader.GetDecimal(6));
        Assert.Equal("n", reader.GetName(0));
    }

    [Fact]
    public async Task PreparedCommandsWork()
    {
        using var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Int32, "n", 7)));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT n FROM t";
        await command.PrepareAsync();

        Assert.Equal(7, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        Assert.Equal(7, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RowReturningCommandThatIsNotASelectWorks()
    {
        // The result shape used to be guessed from the SQL text, so anything not starting with "select" broke.
        using var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Int32, "id", 11)));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE t SET x = 1 RETURNING id";
        Assert.Equal(11, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Theory]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The SQL text comes from the test's own inline data.")]
    [InlineData("SELECT coalesce(a, b) AS x FROM t")]
    [InlineData("SELECT 'a,b' AS x FROM t")]
    [InlineData("SELECT * FROM t")]
    [InlineData("WITH cte AS (SELECT 1) SELECT x FROM cte")]
    public async Task QueriesWithCommasOrNoSelectPrefixKeepTheHandlerShape(string commandText)
    {
        // Each of these used to make the SQL-text parser miscount the columns and corrupt the DataRow stream.
        using var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Int32, "x", 5)));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(1, reader.FieldCount);
        Assert.Equal("x", reader.GetName(0));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(5, reader.GetInt32(0));
    }

    [Fact]
    public async Task DbNullInARowIsSentAsSqlNull()
    {
        using var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Int32, "n", DBNull.Value)));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT n";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public async Task NullInARowIsSentAsSqlNull()
    {
        using var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Text, "s", (object?)null)));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT s";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public async Task AffectedRowCountIsReportedForNonQueryCommands()
    {
        using var server = CreateServer(StaticResult(() => new PostgreSqlQueryResult { CommandTag = "UPDATE", AffectedRowCount = 5 }));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE t SET x = 1";
        Assert.Equal(5, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ACommandTagThatAlreadyCarriesACountIsSentVerbatim()
    {
        // "INSERT 0 1" is a complete tag; appending a row count would have produced "INSERT 0 1 0".
        using var server = CreateServer(StaticResult(() => new PostgreSqlQueryResult { CommandTag = "INSERT 0 1" }));
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();
        await client.SendSimpleQueryAsync("INSERT INTO t VALUES (1)");
        var messages = await client.ReadUntilReadyForQueryAsync();

        var commandComplete = messages.Single(message => message.Type == (byte)'C');
        Assert.Equal("INSERT 0 1", commandComplete.AsText());
    }

    [Fact]
    public async Task TransactionStatusIsReportedToTheClient()
    {
        using var server = CreateServer((context, _) =>
        {
            var commandText = context.CommandText ?? "";
            var result = new PostgreSqlQueryResult
            {
                CommandTag = commandText.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) ? "BEGIN" : "COMMIT",
                TransactionStatus = commandText.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase)
                    ? PostgreSqlTransactionStatus.InTransaction
                    : PostgreSqlTransactionStatus.Idle,
            };

            return ValueTask.FromResult(result);
        });
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();

        await client.SendSimpleQueryAsync("BEGIN");
        var afterBegin = await client.ReadUntilReadyForQueryAsync();
        Assert.Equal((byte)'T', afterBegin[^1].Payload[0]);

        await client.SendSimpleQueryAsync("COMMIT");
        var afterCommit = await client.ReadUntilReadyForQueryAsync();
        Assert.Equal((byte)'I', afterCommit[^1].Payload[0]);
    }

    [Fact]
    public async Task EmptyQueryReturnsEmptyQueryResponse()
    {
        var handlerCalled = false;
        using var server = CreateServer((_, _) =>
        {
            handlerCalled = true;
            return ValueTask.FromResult(new PostgreSqlQueryResult());
        });
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();
        await client.SendSimpleQueryAsync("   ");
        var messages = await client.ReadUntilReadyForQueryAsync();

        Assert.Equal([(byte)'I', (byte)'Z'], messages.Select(message => message.Type));
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task TextFormatParametersAreDecoded()
    {
        // Npgsql binds in binary, so the text decoding path needs a raw client.
        PostgreSqlQueryParameter? captured = null;
        using var server = CreateServer((context, _) =>
        {
            if (context.RequestType == PostgreSqlQueryRequestType.ExtendedQuery && context.Parameters.Count == 1)
            {
                captured = context.Parameters[0];
            }

            return ValueTask.FromResult(Rows(PostgreSqlColumnType.Int32, "n", 1));
        });
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();
        await client.SendParseAsync("s1", "SELECT $1", [16]);
        await client.SendBindWithParametersAsync("", "s1", formatCode: 0, ["TRUE"]);
        await client.SendExecuteAsync("", maxRows: 0);
        await client.SendSyncAsync();
        _ = await client.ReadUntilReadyForQueryAsync();

        Assert.NotNull(captured);

        // PostgreSQL accepts TRUE/t/yes/on/1; only "t" used to decode as true.
        Assert.True(captured.AsBoolean());
        Assert.Equal(0, captured.FormatCode);
        Assert.Equal(16u, captured.TypeOid);
    }

    [Fact]
    public async Task BinaryUuidAndTimestampParametersAreDecoded()
    {
        var expectedGuid = Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2");
        var expectedTimestamp = new DateTime(2024, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var captured = new List<PostgreSqlQueryParameter>();

        using var server = CreateServer((context, _) =>
        {
            if (context.RequestType == PostgreSqlQueryRequestType.ExtendedQuery && context.Parameters.Count == 3)
            {
                captured.Clear();
                captured.AddRange(context.Parameters);
            }

            return ValueTask.FromResult(Rows(PostgreSqlColumnType.Int32, "n", 1));
        });
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @g, @t, @d";
        _ = command.Parameters.Add(new NpgsqlParameter("g", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = expectedGuid });
        _ = command.Parameters.Add(new NpgsqlParameter("t", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = expectedTimestamp });
        _ = command.Parameters.Add(new NpgsqlParameter("d", NpgsqlTypes.NpgsqlDbType.Numeric) { Value = 123.45m });
        _ = await command.ExecuteScalarAsync();

        Assert.HasCount(3, captured);
        Assert.Equal(expectedGuid, captured[0].AsGuid());
        Assert.Equal(expectedTimestamp, captured[1].AsDateTimeOffset()!.Value.UtcDateTime);
        Assert.Equal(123.45m, captured[2].AsDecimal());
    }

    [Fact]
    public async Task AConnectionStaysUsableAfterAQueryError()
    {
        var failNext = true;
        using var server = CreateServer((context, _) =>
        {
            if (context.RequestType != PostgreSqlQueryRequestType.Describe && failNext)
            {
                failNext = false;
                return ValueTask.FromResult(PostgreSqlQueryResult.FromError(new PostgreSqlQueryError { Code = "42601", Message = "syntax error" }));
            }

            return ValueTask.FromResult(Rows(PostgreSqlColumnType.Int32, "n", 3));
        });
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();

        await using (var failing = connection.CreateCommand())
        {
            failing.CommandText = "SELECT bad";
            var exception = await Assert.ThrowsAsync<PostgresException>(async () => await failing.ExecuteScalarAsync());
            Assert.Equal("42601", exception.SqlState);
        }

        await using var succeeding = connection.CreateCommand();
        succeeding.CommandText = "SELECT n";
        Assert.Equal(3, Convert.ToInt32(await succeeding.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AQueryHandlerThatThrowsBecomesAnErrorAndTheConnectionSurvives()
    {
        var shouldThrow = true;
        using var server = CreateServer((context, _) =>
        {
            if (context.RequestType != PostgreSqlQueryRequestType.Describe && shouldThrow)
            {
                shouldThrow = false;
                throw new InvalidOperationException("boom");
            }

            return ValueTask.FromResult(Rows(PostgreSqlColumnType.Int32, "n", 4));
        });
        await server.StartAsync();

        // Driven raw because Npgsql marks a connection Broken on any XX000, which would hide whether the
        // server itself stayed usable.
        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();

        await client.SendParseAsync("s1", "SELECT n");
        await client.SendBindAsync("", "s1");
        await client.SendDescribeAsync((byte)'P', "");
        await client.SendExecuteAsync("", maxRows: 0);
        await client.SendSyncAsync();

        var failed = await client.ReadUntilReadyForQueryAsync();
        var error = Assert.Single(failed, message => message.Type == (byte)'E');
        Assert.Equal("XX000", error.ErrorFields()['C']);
        Assert.Equal((byte)'Z', failed[^1].Type);

        // The same connection continues to work after the handler faulted.
        await client.SendSimpleQueryAsync("SELECT n");
        var succeeded = await client.ReadUntilReadyForQueryAsync();
        Assert.Equal([(byte)'T', (byte)'D', (byte)'C', (byte)'Z'], succeeded.Select(message => message.Type));
        Assert.Equal(["4"], succeeded[1].DataRowValues());
    }

    [Fact]
    public async Task DescribeStatementAndCloseAreHandled()
    {
        using var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Int32, "n", 1)));
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();

        await client.SendParseAsync("s1", "SELECT n");
        await client.SendDescribeAsync((byte)'S', "s1");
        await client.SendSyncAsync();
        var described = await client.ReadUntilReadyForQueryAsync();

        // ParseComplete, ParameterDescription, RowDescription, ReadyForQuery.
        Assert.Equal([(byte)'1', (byte)'t', (byte)'T', (byte)'Z'], described.Select(message => message.Type));

        await client.SendCloseAsync((byte)'S', "s1");
        await client.SendSyncAsync();
        var closed = await client.ReadUntilReadyForQueryAsync();
        Assert.Equal([(byte)'3', (byte)'Z'], closed.Select(message => message.Type));
    }

    [Fact]
    public async Task ExecuteRowLimitSuspendsThePortal()
    {
        using var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Int32, "n", 1, 2, 3, 4, 5)));
        await server.StartAsync();

        using var client = await RawPostgreSqlClient.ConnectAsync(server.Ports[0]);
        await client.AuthenticateClearTextAsync();

        await client.SendParseAsync("s1", "SELECT n");
        await client.SendBindAsync("", "s1");
        await client.SendDescribeAsync((byte)'P', "");
        await client.SendExecuteAsync("", maxRows: 2);
        await client.SendSyncAsync();
        var messages = await client.ReadUntilReadyForQueryAsync();

        Assert.HasCount(2, messages.Where(message => message.Type == (byte)'D').ToArray());
        Assert.Contains(messages, message => message.Type == (byte)'s');
        Assert.DoesNotContain(messages, message => message.Type == (byte)'C');
    }

    [Fact]
    public async Task DescribingAShapeThatDoesNotMatchExecutionIsReportedAsAnError()
    {
        // Better a clear error than DataRows that disagree with the RowDescription already sent.
        using var server = CreateServer((context, _) => ValueTask.FromResult(
            context.RequestType == PostgreSqlQueryRequestType.Describe
                ? new PostgreSqlQueryResult()
                : Rows(PostgreSqlColumnType.Int32, "n", 1)));
        await server.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString(server.Ports[0]));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT n";
        var exception = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
        Assert.Equal("XX000", exception.SqlState);
    }

    [Fact]
    public async Task DisposeIsIdempotentAndStopAsyncDrains()
    {
        var server = CreateServer(StaticResult(() => Rows(PostgreSqlColumnType.Int32, "n", 1)));
        await server.StartAsync();
        Assert.Single(server.Ports);

        await server.StopAsync(CancellationToken.None);
        server.Dispose();
        server.Dispose();
    }

    [Fact]
    public async Task AFailedBindLeavesNothingListening()
    {
        using var occupied = CreateServer(StaticResult(() => new PostgreSqlQueryResult()));
        await occupied.StartAsync();
        var takenPort = occupied.Ports[0];

        var options = new PostgreSqlServerOptions { AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword };
        _ = options.AddTcpListener(0, IPAddress.Loopback);
        _ = options.AddTcpListener(takenPort, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (_, _) => ValueTask.FromResult(PostgreSqlAuthenticationResult.Success()),
            (_, _) => ValueTask.FromResult(new PostgreSqlQueryResult()));

        _ = await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync());

        // Binding is all-or-nothing, so no port should be left bound and reported.
        Assert.Empty(server.Ports);
    }
}
