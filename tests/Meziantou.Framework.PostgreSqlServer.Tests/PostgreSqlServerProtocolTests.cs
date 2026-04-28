using System.Globalization;
using System.Net;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework.PostgreSql;
using Meziantou.Framework.PostgreSql.Handler;
using Npgsql;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.PostgreSql.Tests;

[RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
public sealed class PostgreSqlServerProtocolTests
{
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

        await using var connection = new NpgsqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 /* {Marker} */";
        command.AllResultTypesAreUnknown = true;
        var result = await command.ExecuteScalarAsync();

        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(123, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.True(capturedContext.RequestType is PostgreSqlQueryRequestType.SimpleQuery or PostgreSqlQueryRequestType.ExtendedQuery);
        Assert.Contains(Marker, capturedContext.CommandText, StringComparison.Ordinal);
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
                if (context.RequestType == PostgreSqlQueryRequestType.ExtendedQuery &&
                    context.Parameters.Count == 2 &&
                    context.Parameters[0].AsInt32() == 42 &&
                    context.Parameters[1].AsBoolean() == true)
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResult(PostgreSqlColumnType.Int32, 42));
                }

                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
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
                if (context.RequestType == PostgreSqlQueryRequestType.ExtendedQuery &&
                    context.Parameters.Count == 1 &&
                    ReferenceEquals(context.Parameters[0].Value, DBNull.Value) &&
                    context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
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
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
                {
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

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        _ = await Assert.ThrowsAnyAsync<Exception>(() => command.ExecuteNonQueryAsync(cancellationTokenSource.Token));
        Assert.True(await queryCanceledTask.Task.WaitAsync(TimeSpan.FromSeconds(5)));
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
        return $"Host={IPAddress.Loopback};Port={port};Username={userName};Password={password};Database={database};SSL Mode={sslMode};Trust Server Certificate={trustServerCertificateValue};Pooling=false;Timeout=5;Command Timeout=5;Server Compatibility Mode=NoTypeLoading";
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
