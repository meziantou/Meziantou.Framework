using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.PostgreSql.Handler;
using Npgsql;
using Meziantou.Xunit;

namespace Meziantou.Framework.PostgreSql.Tests;

[RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
public sealed class PostgreSqlServerProtocolTests
{
    [Fact]
    public async Task ReadmeExample_ReadingParameters()
    {
        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(PostgreSqlAuthenticationResult.Success()),
            (context, cancellationToken) =>
            {
                if (context.RequestType == PostgreSqlQueryRequestType.Describe)
                {
                    return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 0));
                }

                #region PostgreSqlServer_ReadingParameters
                var id = context.Parameters[0].AsInt32();
                #endregion

                return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, id ?? -1));
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @id";
        command.AllResultTypesAreUnknown = true;
        _ = command.Parameters.Add(new NpgsqlParameter("id", NpgsqlTypes.NpgsqlDbType.Integer) { Value = 42 });
        var result = await command.ExecuteScalarAsync();

        Assert.Equal(42, Convert.ToInt32(result, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ReadmeExample_ErrorsAndCommandTags()
    {
        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(PostgreSqlAuthenticationResult.Success()),
            (context, cancellationToken) =>
            {
                #region PostgreSqlServer_ErrorsAndCommandTags
                if (context.CommandText?.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) != true)
                {
                    // An error the client sees as a PostgresException, without terminating the connection.
                    return ValueTask.FromResult(PostgreSqlQueryResult.FromError(new PostgreSqlQueryError
                    {
                        Code = "42601",
                        Message = "syntax error",
                    }));
                }

                // Affected rows for a command that returns no result set.
                return ValueTask.FromResult(new PostgreSqlQueryResult
                {
                    CommandTag = "UPDATE",
                    AffectedRowCount = 5,
                    TransactionStatus = PostgreSqlTransactionStatus.InTransaction,
                });
                #endregion
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var invalidCommand = connection.CreateCommand();
        invalidCommand.CommandText = "SELEC 1";
        var exception = await Assert.ThrowsAsync<PostgresException>(() => invalidCommand.ExecuteNonQueryAsync());
        Assert.Equal("42601", exception.SqlState);

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE customers SET name = 'a'";
        Assert.Equal(5, await updateCommand.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Documentation_GettingStarted()
    {
        #region PostgreSqlServer_GettingStarted
        var options = new PostgreSqlServerOptions();
        options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback); // 0: the system picks a free port

        await using var server = new PostgreSqlServer(
            options,
            authenticationHandler: (context, cancellationToken) =>
            {
                // SCRAM-SHA-256 by default: ValidatePassword checks the proof the client sent
                var result = context.UserName == "app" && context.ValidatePassword("Password123!")
                    ? PostgreSqlAuthenticationResult.Success()
                    : PostgreSqlAuthenticationResult.Fail("password authentication failed");
                return ValueTask.FromResult(result);
            },
            queryHandler: (context, cancellationToken) =>
            {
                var resultSet = new PostgreSqlResultSet();
                resultSet.Columns.Add(new PostgreSqlColumn("message", PostgreSqlColumnType.Text));

                // Describe asks for the columns only, before the execution
                if (context.RequestType is not PostgreSqlQueryRequestType.Describe)
                {
                    resultSet.Rows.Add([$"You sent: {context.CommandText}"]);
                }

                var result = new PostgreSqlQueryResult();
                result.ResultSets.Add(resultSet);
                return ValueTask.FromResult(result);
            });
        await server.StartAsync();

        var port = server.Ports[0];
        await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=app;Password=Password123!;Server Compatibility Mode=NoTypeLoading");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var message = await command.ExecuteScalarAsync();
        Assert.Equal("You sent: SELECT 1", message);
        #endregion
    }

    [Fact]
    public async Task Documentation_Authentication()
    {
        var options = new PostgreSqlServerOptions();
        options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);

        #region PostgreSqlServer_Authentication
        var users = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app"] = "Password123!",
            ["reporting"] = "Reporting456!",
        };

        PostgreSqlAuthenticationDelegate authenticate = (context, cancellationToken) =>
        {
            if (context.UserName is null || !users.TryGetValue(context.UserName, out var password) || !context.ValidatePassword(password))
            {
                return ValueTask.FromResult(PostgreSqlAuthenticationResult.Fail($"password authentication failed for user \"{context.UserName}\""));
            }

            if (context.Database is not "sales")
            {
                // 3D000: invalid_catalog_name
                return ValueTask.FromResult(PostgreSqlAuthenticationResult.Fail($"database \"{context.Database}\" does not exist", "3D000"));
            }

            // Every startup parameter the client sent, such as application_name
            _ = context.StartupParameters.TryGetValue("application_name", out var applicationName);
            return ValueTask.FromResult(PostgreSqlAuthenticationResult.Success());
        };
        #endregion

        await using var server = new PostgreSqlServer(options, authenticate, (context, cancellationToken) => ValueTask.FromResult(new PostgreSqlQueryResult()));
        await server.StartAsync();
        var port = server.Ports[0];

        await using (var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=reporting;Password=Reporting456!;Database=sales;Server Compatibility Mode=NoTypeLoading;Pooling=false"))
        {
            await connection.OpenAsync();
        }

        await using (var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=app;Password=wrong;Database=sales;Server Compatibility Mode=NoTypeLoading;Pooling=false"))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync());
            Assert.Equal("28P01", exception.SqlState);
        }

        await using (var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=app;Password=Password123!;Database=other;Server Compatibility Mode=NoTypeLoading;Pooling=false"))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync());
            Assert.Equal("3D000", exception.SqlState);
        }
    }

    [Fact]
    public async Task Documentation_Parameters()
    {
        var options = new PostgreSqlServerOptions();
        options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);

        #region PostgreSqlServer_Parameters
        var customers = new Dictionary<int, string> { [1] = "Alice", [2] = "Bob" };

        PostgreSqlQueryDelegate query = (context, cancellationToken) =>
        {
            var resultSet = new PostgreSqlResultSet();
            resultSet.Columns.Add(new PostgreSqlColumn("id", PostgreSqlColumnType.Int32));
            resultSet.Columns.Add(new PostgreSqlColumn("name", PostgreSqlColumnType.Text));

            var result = new PostgreSqlQueryResult();
            result.ResultSets.Add(resultSet);

            // The same columns for Describe and for the execution; Describe gets no rows
            if (context.RequestType is PostgreSqlQueryRequestType.Describe)
            {
                return ValueTask.FromResult(result);
            }

            // Parameters are positional: $1, $2, ...
            var id = context.Parameters.Count > 0 ? context.Parameters[0].AsInt32() : null;
            if (id is not null && customers.TryGetValue(id.Value, out var name))
            {
                resultSet.Rows.Add([id.Value, name]);
            }

            return ValueTask.FromResult(result);
        };
        #endregion

        await using var server = new PostgreSqlServer(options, (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!") ? PostgreSqlAuthenticationResult.Success() : PostgreSqlAuthenticationResult.Fail("invalid")), query);
        await server.StartAsync();

        await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={server.Ports[0]};Username=app;Password=Password123!;Server Compatibility Mode=NoTypeLoading;Pooling=false");
        await connection.OpenAsync();

        #region PostgreSqlServer_ParametersClient
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name FROM customers WHERE id = $1";
        command.Parameters.Add(new NpgsqlParameter { Value = 2 });

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal("Bob", reader.GetString(1));
        #endregion
    }

    [Fact]
    public async Task Documentation_ColumnTypes()
    {
        var options = new PostgreSqlServerOptions();
        options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);

        #region PostgreSqlServer_ColumnTypes
        PostgreSqlQueryDelegate query = (context, cancellationToken) =>
        {
            var resultSet = new PostgreSqlResultSet();
            resultSet.Columns.Add(new PostgreSqlColumn("id", PostgreSqlColumnType.Uuid));
            resultSet.Columns.Add(new PostgreSqlColumn("quantity", PostgreSqlColumnType.Int64));
            resultSet.Columns.Add(new PostgreSqlColumn("price", PostgreSqlColumnType.Numeric));
            resultSet.Columns.Add(new PostgreSqlColumn("in_stock", PostgreSqlColumnType.Boolean));
            resultSet.Columns.Add(new PostgreSqlColumn("released", PostgreSqlColumnType.Date));
            resultSet.Columns.Add(new PostgreSqlColumn("updated_at", PostgreSqlColumnType.TimestampTz));
            resultSet.Columns.Add(new PostgreSqlColumn("comment", PostgreSqlColumnType.Text));

            if (context.RequestType is not PostgreSqlQueryRequestType.Describe)
            {
                resultSet.Rows.Add(
                [
                    Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2"),
                    12L,
                    19.99m,
                    true,
                    new DateOnly(2026, 3, 1),
                    new DateTimeOffset(2026, 10, 29, 8, 30, 0, TimeSpan.Zero),
                    null, // SQL NULL
                ]);
            }

            var result = new PostgreSqlQueryResult();
            result.ResultSets.Add(resultSet);
            return ValueTask.FromResult(result);
        };
        #endregion

        await using var server = new PostgreSqlServer(options, (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!") ? PostgreSqlAuthenticationResult.Success() : PostgreSqlAuthenticationResult.Fail("invalid")), query);
        await server.StartAsync();

        await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={server.Ports[0]};Username=app;Password=Password123!;Server Compatibility Mode=NoTypeLoading;Pooling=false");
        await connection.OpenAsync();

        #region PostgreSqlServer_ColumnTypesClient
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM products";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2"), reader.GetGuid(0));
        Assert.Equal(12L, reader.GetInt64(1));
        Assert.Equal(19.99m, reader.GetDecimal(2));
        Assert.True(reader.GetBoolean(3));
        Assert.Equal(new DateOnly(2026, 3, 1), reader.GetFieldValue<DateOnly>(4));
        Assert.Equal(new DateTime(2026, 10, 29, 8, 30, 0, DateTimeKind.Utc), reader.GetDateTime(5));
        Assert.True(reader.IsDBNull(6));
        #endregion
    }

    [Fact]
    public async Task Documentation_Notices()
    {
        var options = new PostgreSqlServerOptions();
        options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);

        #region PostgreSqlServer_Notices
        PostgreSqlQueryDelegate query = (context, cancellationToken) =>
        {
            var result = new PostgreSqlQueryResult
            {
                CommandTag = "DELETE",
                AffectedRowCount = 3,
            };
            result.Notices.Add("3 rows deleted, 2 rows kept");
            return ValueTask.FromResult(result);
        };
        #endregion

        await using var server = new PostgreSqlServer(options, (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!") ? PostgreSqlAuthenticationResult.Success() : PostgreSqlAuthenticationResult.Fail("invalid")), query);
        await server.StartAsync();

        await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={server.Ports[0]};Username=app;Password=Password123!;Server Compatibility Mode=NoTypeLoading;Pooling=false");
        await connection.OpenAsync();

        #region PostgreSqlServer_NoticesClient
        var notices = new List<string>();
        connection.Notice += (sender, e) => notices.Add(e.Notice.MessageText);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE expired";
        var affectedRows = await command.ExecuteNonQueryAsync();

        Assert.Equal(3, affectedRows);
        Assert.Equal(["3 rows deleted, 2 rows kept"], notices);
        #endregion
    }

    [Fact]
    public async Task Documentation_Cancellation()
    {
        var options = new PostgreSqlServerOptions();
        options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        #region PostgreSqlServer_Cancellation
        PostgreSqlQueryDelegate query = async (context, cancellationToken) =>
        {
            if (context.RequestType is not PostgreSqlQueryRequestType.Describe && context.CommandText == "SELECT pg_sleep(3600)")
            {
                handlerStarted.TrySetResult();

                // Canceled when the client cancels the command, or when the server stops
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new PostgreSqlQueryResult();
        };
        #endregion

        await using var server = new PostgreSqlServer(options, (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!") ? PostgreSqlAuthenticationResult.Success() : PostgreSqlAuthenticationResult.Fail("invalid")), query);
        await server.StartAsync();

        await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={server.Ports[0]};Username=app;Password=Password123!;Server Compatibility Mode=NoTypeLoading;Pooling=false");
        await connection.OpenAsync();

        using var cancellationTokenSource = new CancellationTokenSource();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_sleep(3600)";
        var executeTask = command.ExecuteNonQueryAsync(cancellationTokenSource.Token);
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellationTokenSource.CancelAsync();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => executeTask);
        Assert.Equal("57014", Assert.IsType<PostgresException>(exception.InnerException).SqlState);

        // The connection is still usable
        await using var nextCommand = connection.CreateCommand();
        nextCommand.CommandText = "SELECT 1";
        // Npgsql reports -1 for the default "OK 0" command tag
        Assert.Equal(-1, await nextCommand.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Documentation_Tls()
    {
        using var certificate = TestCertificate.Create();
        var pfxPath = certificate.PfxPath;
        var pfxPassword = certificate.PfxPassword;

        #region PostgreSqlServer_RequireTls
        var options = new PostgreSqlServerOptions
        {
            TlsPfxPath = pfxPath,
            TlsPfxPassword = pfxPassword,
            RequireEncryption = true, // reject the clients that do not upgrade to TLS
        };
        options.AddTcpListener(port: 0, bindAddress: IPAddress.Loopback);
        #endregion

        await using var server = new PostgreSqlServer(options, (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!") ? PostgreSqlAuthenticationResult.Success() : PostgreSqlAuthenticationResult.Fail("invalid")), (context, cancellationToken) => ValueTask.FromResult(new PostgreSqlQueryResult()));
        await server.StartAsync();
        var port = server.Ports[0];

        #region PostgreSqlServer_TlsClient
        // Trust Server Certificate accepts a self-signed certificate: use it in tests only
        await using var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=app;Password=Password123!;SSL Mode=Require;Trust Server Certificate=true;Server Compatibility Mode=NoTypeLoading");
        await connection.OpenAsync();
        #endregion

        await using var plaintextConnection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Username=app;Password=Password123!;SSL Mode=Disable;Server Compatibility Mode=NoTypeLoading;Pooling=false");
        var exception = await Assert.ThrowsAsync<PostgresException>(() => plaintextConnection.OpenAsync());
        Assert.Equal("28000", exception.SqlState);
    }

    [Fact]
    public async Task Npgsql_ClearTextAuthentication_UsesCallbackValidation()
    {
        const string UserName = "app_user";
        const string Password = "Password123!";
        var authenticationContextTask = new TaskCompletionSource<PostgreSqlAuthenticationContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) =>
            {
                _ = cancellationToken;
                authenticationContextTask.TrySetResult(context);
                return ValueTask.FromResult(context.ValidatePassword(Password)
                    ? PostgreSqlAuthenticationResult.Success()
                    : PostgreSqlAuthenticationResult.Fail("invalid password"));
            },
            (context, cancellationToken) => ValueTask.FromResult(new PostgreSqlQueryResult()));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port, userName: UserName, password: Password));
        await connection.OpenAsync();

        var capturedContext = await authenticationContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(UserName, capturedContext.UserName);
        Assert.Equal(PostgreSqlAuthenticationMethod.ClearTextPassword, capturedContext.Method);
    }

    [Fact]
    public async Task Npgsql_Md5Authentication_UsesCallbackValidation()
    {
        const string Password = "Password123!";
        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.Md5Password,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) =>
            {
                _ = cancellationToken;
                return ValueTask.FromResult(context.ValidatePassword(Password)
                    ? PostgreSqlAuthenticationResult.Success()
                    : PostgreSqlAuthenticationResult.Fail("invalid password"));
            },
            (context, cancellationToken) => ValueTask.FromResult(new PostgreSqlQueryResult()));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port, password: Password));
        await connection.OpenAsync();
    }

    [Fact]
    public void ScramAuthenticationContext_ValidatePassword_ComputesServerProof()
    {
        const string Password = "Password123!";
        var salt = Convert.FromBase64String("W22ZaJ0SNY7soEsUEjb6gQ==");
        const int IterationCount = 4096;
        const string ClientFirstBare = "n=app,r=fyko+d2lbbFgONRv9qkxdawL";
        const string ServerFirst = "r=fyko+d2lbbFgONRv9qkxdawL3rfcNHYJY1ZVvWVs7j,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4096";
        const string ClientFinalWithoutProof = "c=biws,r=fyko+d2lbbFgONRv9qkxdawL3rfcNHYJY1ZVvWVs7j";
        var authMessage = $"{ClientFirstBare},{ServerFirst},{ClientFinalWithoutProof}";

        var context = new PostgreSqlAuthenticationContext
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Method = PostgreSqlAuthenticationMethod.ScramSha256,
            UserName = "app",
        };
        SetNonPublicProperty(context, "ScramSalt", salt);
        SetNonPublicProperty(context, "ScramIterationCount", IterationCount);
        SetNonPublicProperty(context, "ScramClientProof", CreateScramClientProof(Password, salt, IterationCount, authMessage));
        SetNonPublicProperty(context, "ScramAuthMessage", authMessage);

        Assert.True(context.ValidatePassword(Password));
        Assert.False(context.ValidatePassword("wrong-password"));
    }

    [Fact]
    public async Task Npgsql_ScramAuthentication_UsesCallbackValidation()
    {
        const string Password = "Password123!";
        var authenticationContextTask = new TaskCompletionSource<PostgreSqlAuthenticationContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ScramSha256,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) =>
            {
                _ = cancellationToken;
                authenticationContextTask.TrySetResult(context);
                return ValueTask.FromResult(context.ValidatePassword(Password)
                    ? PostgreSqlAuthenticationResult.Success()
                    : PostgreSqlAuthenticationResult.Fail("invalid password"));
            },
            (context, cancellationToken) => ValueTask.FromResult(new PostgreSqlQueryResult()));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port, password: Password));
        await connection.OpenAsync();

        var capturedContext = await authenticationContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PostgreSqlAuthenticationMethod.ScramSha256, capturedContext.Method);
    }

    [Fact]
    public async Task Npgsql_SimpleQuery_UsesSimpleRequestType()
    {
        const string Marker = "simple-query-marker";
        var queryContextTask = new TaskCompletionSource<PostgreSqlQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password")),
            (context, cancellationToken) =>
            {
                _ = cancellationToken;
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 123));
                }

                return ValueTask.FromResult(new PostgreSqlQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        // Npgsql always uses the extended protocol, so the simple query path needs a raw client.
        using var client = await RawPostgreSqlClient.ConnectAsync(port);
        await client.AuthenticateClearTextAsync();
        await client.SendSimpleQueryAsync($"SELECT 1 /* {Marker} */");
        var messages = await client.ReadUntilReadyForQueryAsync();

        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PostgreSqlQueryRequestType.SimpleQuery, capturedContext.RequestType);
        Assert.Contains(Marker, capturedContext.CommandText);

        // A simple query describes its own result: RowDescription, DataRow, CommandComplete, ReadyForQuery.
        Assert.Equal([(byte)'T', (byte)'D', (byte)'C', (byte)'Z'], messages.Select(message => message.Type));
        Assert.Equal(["123"], messages[1].DataRowValues());
        Assert.Equal("SELECT 1", messages[2].AsText());
    }

    [Fact]
    public async Task Npgsql_ExtendedQuery_WithParameters_UsesExtendedRequestType()
    {
        const string Marker = "extended-query-marker";
        var queryContextTask = new TaskCompletionSource<PostgreSqlQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password")),
            (context, cancellationToken) =>
            {
                _ = cancellationToken;
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) != true)
                {
                    return ValueTask.FromResult(new PostgreSqlQueryResult());
                }

                // Describe must announce the same shape the execution produces.
                if (context.RequestType == PostgreSqlQueryRequestType.Describe)
                {
                    return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 42));
                }

                if (context.RequestType == PostgreSqlQueryRequestType.ExtendedQuery &&
                    context.Parameters.Count == 2 &&
                    context.Parameters[0].AsInt32() == 42 &&
                    context.Parameters[1].AsBoolean() == true)
                {
                    queryContextTask.TrySetResult(context);
                }

                return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 42));
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT @value WHERE @flag /* {Marker} */";
        command.AllResultTypesAreUnknown = true;
        _ = command.Parameters.Add(new NpgsqlParameter("value", NpgsqlTypes.NpgsqlDbType.Integer) { Value = 42 });
        _ = command.Parameters.Add(new NpgsqlParameter("flag", NpgsqlTypes.NpgsqlDbType.Boolean) { Value = true });
        var result = await command.ExecuteScalarAsync();

        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(PostgreSqlQueryRequestType.ExtendedQuery, capturedContext.RequestType);
        Assert.Equal(2, capturedContext.Parameters.Count);

        var firstParameter = capturedContext.Parameters[0];
        Assert.Equal(PostgreSqlColumnType.Int32, firstParameter.Type);
        Assert.Equal(42, firstParameter.AsInt32());

        var secondParameter = capturedContext.Parameters[1];
        Assert.Equal(PostgreSqlColumnType.Boolean, secondParameter.Type);
        Assert.True(secondParameter.AsBoolean() is true);
    }

    [Fact]
    public async Task Npgsql_ExtendedQuery_WithNullParameter_UsesDBNullValue()
    {
        const string Marker = "extended-query-null-parameter-marker";
        var queryContextTask = new TaskCompletionSource<PostgreSqlQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password")),
            (context, cancellationToken) =>
            {
                _ = cancellationToken;
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) != true)
                {
                    return ValueTask.FromResult(new PostgreSqlQueryResult());
                }

                // Describe must announce the same shape the execution produces.
                if (context.RequestType == PostgreSqlQueryRequestType.Describe)
                {
                    return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 42));
                }

                if (context.RequestType == PostgreSqlQueryRequestType.ExtendedQuery &&
                    context.Parameters.Count == 1 &&
                    ReferenceEquals(context.Parameters[0].Value, DBNull.Value))
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 42));
                }

                return ValueTask.FromResult(new PostgreSqlQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT @value /* {Marker} */";
        command.AllResultTypesAreUnknown = true;
        _ = command.Parameters.Add(new NpgsqlParameter("value", NpgsqlTypes.NpgsqlDbType.Integer) { Value = DBNull.Value });
        var result = await command.ExecuteScalarAsync();

        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(PostgreSqlQueryRequestType.ExtendedQuery, capturedContext.RequestType);

        var parameter = Assert.Single(capturedContext.Parameters);
        Assert.Equal(PostgreSqlColumnType.Int32, parameter.Type);
        Assert.Same(DBNull.Value, parameter.Value);
        Assert.True(parameter.IsNull);
        Assert.Null(parameter.RawValue);
        Assert.Null(parameter.AsString());
        Assert.Null(parameter.AsInt32());
        Assert.Null(parameter.AsInt64());
        Assert.Null(parameter.AsBoolean());
        Assert.Null(parameter.AsDouble());
        Assert.Null(parameter.AsDecimal());
        Assert.Null(parameter.AsBinary());
        Assert.Null(parameter.AsJson());
    }

    [Fact]
    public async Task Npgsql_RequireTls_WithPfxCertificate_Connects()
    {
        using var tlsCertificateFiles = CreateTlsCertificateFiles();

        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
            TlsPfxPath = tlsCertificateFiles.PfxPath,
            TlsPfxPassword = tlsCertificateFiles.PfxPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password")),
            (context, cancellationToken) => ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 1)));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port, sslMode: "Require"));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.AllResultTypesAreUnknown = true;
        var value = await command.ExecuteScalarAsync();
        Assert.Equal(1, Convert.ToInt32(value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Npgsql_Cancellation_CancelsRunningQuery()
    {
        const string Marker = "cancel-query-marker";
        var queryCanceledTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryStartedTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password")),
            async (context, cancellationToken) =>
            {
                if (context.RequestType != PostgreSqlQueryRequestType.Describe &&
                    context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
                {
                    queryStartedTask.TrySetResult();
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        queryCanceledTask.TrySetResult(true);
                        throw;
                    }
                }

                return CreateScalarResult(PostgreSqlColumnType.Int32, 1);
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 /* {Marker} */";
        command.AllResultTypesAreUnknown = true;

        // Cancel once the handler has actually started, rather than racing a wall-clock timer against the round trip.
        using var cancellationTokenSource = new CancellationTokenSource();
        var executeTask = command.ExecuteNonQueryAsync(cancellationTokenSource.Token);
        await queryStartedTask.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellationTokenSource.CancelAsync();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => executeTask);
        Assert.True(await queryCanceledTask.Task.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The SQL text is generated from enum values controlled by the test itself.")]
    public async Task Npgsql_QueryResult_CoversColumnTypes()
    {
        const string Marker = "all-types-marker";
        var guidValue = Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2", CultureInfo.InvariantCulture);
        var valuesByType = new Dictionary<PostgreSqlColumnType, (object Value, string ExpectedString)>
        {
            [PostgreSqlColumnType.Boolean] = (true, "t"),
            [PostgreSqlColumnType.Int16] = ((short)2, "2"),
            [PostgreSqlColumnType.Int32] = (3, "3"),
            [PostgreSqlColumnType.Int64] = (4L, "4"),
            [PostgreSqlColumnType.Single] = (1.25f, "1.25"),
            [PostgreSqlColumnType.Double] = (2.5d, "2.5"),
            [PostgreSqlColumnType.Numeric] = (123.45m, "123.45"),
            [PostgreSqlColumnType.Text] = ("text-value", "text-value"),
            [PostgreSqlColumnType.Bytea] = (new byte[] { 0x10, 0x20, 0x30 }, "\\x102030"),
            [PostgreSqlColumnType.Uuid] = (guidValue, guidValue.ToString("D", CultureInfo.InvariantCulture)),
            [PostgreSqlColumnType.Date] = (new DateOnly(2024, 05, 01), "2024-05-01"),
            [PostgreSqlColumnType.Timestamp] = (new DateTime(2024, 05, 01, 12, 34, 56, DateTimeKind.Utc), "2024-05-01 12:34:56.000000"),
            [PostgreSqlColumnType.TimestampTz] = (new DateTimeOffset(2024, 05, 01, 12, 34, 56, TimeSpan.FromHours(2)), "2024-05-01 12:34:56.000000+02:00"),
            [PostgreSqlColumnType.Json] = ("{\"value\":42}", "{\"value\":42}"),
            [PostgreSqlColumnType.Jsonb] = ("{\"value\":43}", "{\"value\":43}"),
        };

        var options = new PostgreSqlServerOptions
        {
            AuthenticationMethod = PostgreSqlAuthenticationMethod.ClearTextPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new PostgreSqlServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(context.ValidatePassword("Password123!")
                ? PostgreSqlAuthenticationResult.Success()
                : PostgreSqlAuthenticationResult.Fail("invalid password")),
            (context, cancellationToken) =>
            {
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
                {
                    var type = ParseRequestedType(context.CommandText);
                    var (value, _) = valuesByType[type];
                    var resultSet = new PostgreSqlResultSet();
                    resultSet.Columns.Add(new PostgreSqlColumn("value", type, isNullable: false));
                    resultSet.Rows.Add([value]);

                    var result = new PostgreSqlQueryResult();
                    result.ResultSets.Add(resultSet);
                    return ValueTask.FromResult(result);
                }

                return ValueTask.FromResult(new PostgreSqlQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new NpgsqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        foreach (var (type, (_, expectedString)) in valuesByType)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT 1 /* {Marker}:{type} */";
            command.AllResultTypesAreUnknown = true;
            var value = await command.ExecuteScalarAsync();
            Assert.Equal(expectedString, Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }

    private static PostgreSqlQueryResult CreateScalarResult(PostgreSqlColumnType type, object value)
    {
        var resultSet = new PostgreSqlResultSet();
        resultSet.Columns.Add(new PostgreSqlColumn("value", type, isNullable: false));
        resultSet.Rows.Add([value]);

        var result = new PostgreSqlQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    }

    private static PostgreSqlColumnType ParseRequestedType(string commandText)
    {
        var markerIndex = commandText.IndexOf("all-types-marker:", StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException("Type marker not found.");
        }

        markerIndex += "all-types-marker:".Length;
        var endIndex = commandText.IndexOf("*/", markerIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new InvalidOperationException("Type marker end not found.");
        }

        var value = commandText.Substring(markerIndex, endIndex - markerIndex).Trim();
        if (Enum.TryParse<PostgreSqlColumnType>(value, ignoreCase: true, out var result))
        {
            return result;
        }

        throw new InvalidOperationException($"Unsupported marker type '{value}'.");
    }

    private static byte[] CreateScramClientProof(string password, byte[] salt, int iterationCount, string authMessage)
    {
        var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterationCount, HashAlgorithmName.SHA256, 32);
        var clientKey = HMACSHA256.HashData(saltedPassword, Encoding.UTF8.GetBytes("Client Key"));
        var storedKey = SHA256.HashData(clientKey);
        var clientSignature = HMACSHA256.HashData(storedKey, Encoding.UTF8.GetBytes(authMessage));
        var proof = new byte[clientKey.Length];
        for (var i = 0; i < clientKey.Length; i++)
        {
            proof[i] = (byte)(clientKey[i] ^ clientSignature[i]);
        }

        return proof;
    }

    private static void SetNonPublicProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (property is null)
        {
            throw new InvalidOperationException($"Property '{propertyName}' not found.");
        }

        property.SetValue(target, value);
    }

    private static string CreateConnectionString(
        int port,
        string userName = "app",
        string password = "Password123!",
        string database = "postgres",
        string sslMode = "Disable",
        bool trustServerCertificate = true)
    {
        var trustServerCertificateValue = trustServerCertificate ? "true" : "false";
        return $"Host={IPAddress.Loopback};Port={port};Username={userName};Password={password};Database={database};SSL Mode={sslMode};Trust Server Certificate={trustServerCertificateValue};Pooling=false;Timeout=30;Command Timeout=30;Server Compatibility Mode=NoTypeLoading";
    }

    private static TlsCertificateFiles CreateTlsCertificateFiles()
    {
        var directoryPath = Path.Combine(AppContext.BaseDirectory, "certificates", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directoryPath);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        const string PfxPassword = "Password123!";

        var pfxPath = Path.Combine(directoryPath, "server.pfx");
        File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, PfxPassword));

        return new TlsCertificateFiles(directoryPath, pfxPath, PfxPassword);
    }

    private sealed class TlsCertificateFiles : IDisposable
    {
        private readonly string _directoryPath;

        public TlsCertificateFiles(string directoryPath, string pfxPath, string pfxPassword)
        {
            _directoryPath = directoryPath;
            PfxPath = pfxPath;
            PfxPassword = pfxPassword;
        }

        public string PfxPath { get; }

        public string PfxPassword { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
