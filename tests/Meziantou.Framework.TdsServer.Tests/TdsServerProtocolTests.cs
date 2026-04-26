using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.Tds;
using Meziantou.Framework.Tds.Handler;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Meziantou.Framework.Tds.Tests;

public sealed class TdsServerProtocolTests
{
    [Fact]
    public async Task SqlClient_AuthenticationCallback_ReceivesCredentials()
    {
        const string userName = "sa";
        const string password = "Password123!";
        var authenticationContextTask = new TaskCompletionSource<TdsAuthenticationContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) =>
            {
                authenticationContextTask.TrySetResult(context);
                return ValueTask.FromResult(TdsAuthenticationResult.Success("master"));
            },
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port, userName, password));
        await connection.OpenAsync();

        var capturedContext = await authenticationContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(userName, capturedContext.UserName);
        Assert.NotNull(capturedContext.Password);
        Assert.Equal("master", capturedContext.Database);
    }

    [Fact]
    public async Task SqlClient_TextQuery_WithoutParameters_UsesSqlBatch()
    {
        const string marker = "TextQueryWithoutParametersMarker";
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.CommandText?.Contains(marker, StringComparison.Ordinal) == true)
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 123));
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 /* {marker} */";

        var result = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(123, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(TdsQueryRequestType.SqlBatch, capturedContext.RequestType);
        Assert.Contains(marker, capturedContext.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlClient_TextQuery_WithParameters_UsesRpc()
    {
        const string marker = "TextQueryWithParametersMarker";
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc &&
                    string.Equals(context.ProcedureName, "sp_executesql", StringComparison.OrdinalIgnoreCase) &&
                    context.Parameters.Any(parameter => parameter.Value.AsString()?.Contains(marker, StringComparison.Ordinal) == true) &&
                    HasIntParameter(context.Parameters, 42))
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 42));
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT @value /* {marker} */";
        _ = command.Parameters.Add(new SqlParameter("@value", SqlDbType.Int) { Value = 42 });

        var result = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(42, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(TdsQueryRequestType.Rpc, capturedContext.RequestType);
        Assert.Equal("sp_executesql", capturedContext.ProcedureName, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(capturedContext.Parameters, parameter => IsExpectedIntParameter(parameter.Value, 42));
    }

    [Fact]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The stored procedure name is generated within the test and not user-controlled.")]
    public async Task SqlClient_StoredProcedure_WithoutParameters_UsesRpc()
    {
        var procedureName = "proc_without_params_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc && string.Equals(context.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase))
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(new TdsQueryResult());
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = procedureName;

        _ = await command.ExecuteNonQueryAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(TdsQueryRequestType.Rpc, capturedContext.RequestType);
        Assert.Equal(procedureName, capturedContext.ProcedureName, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(capturedContext.Parameters);
    }

    [Fact]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The stored procedure name is generated within the test and not user-controlled.")]
    public async Task SqlClient_StoredProcedure_WithParameters_UsesRpc()
    {
        var procedureName = "proc_with_params_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc &&
                    string.Equals(context.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase) &&
                    HasIntParameter(context.Parameters, 7) &&
                    context.Parameters.Any(parameter => string.Equals(parameter.Value.AsString(), "sample", StringComparison.Ordinal)))
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 7));
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = procedureName;
        _ = command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = 7 });
        _ = command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 50) { Value = "sample" });

        var result = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(7, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(TdsQueryRequestType.Rpc, capturedContext.RequestType);
        Assert.Equal(procedureName, capturedContext.ProcedureName, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(capturedContext.Parameters, parameter => IsExpectedIntParameter(parameter.Value, 7));
        Assert.Contains(capturedContext.Parameters, parameter => string.Equals(parameter.Value.AsString(), "sample", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SqlClient_EncryptTrue_WithPfxCertificate_Connects()
    {
        using var tlsCertificateFiles = CreateTlsCertificateFiles();

        var options = new TdsServerOptions
        {
            TlsPfxPath = tlsCertificateFiles.PfxPath,
            TlsPfxPassword = tlsCertificateFiles.PfxPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1)));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port, encrypt: "True"));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var value = await command.ExecuteScalarAsync();

        Assert.Equal(1, Convert.ToInt32(value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SqlClient_EncryptTrue_WithPemCertificate_Connects()
    {
        using var tlsCertificateFiles = CreateTlsCertificateFiles();

        var options = new TdsServerOptions
        {
            TlsPemCertificatePath = tlsCertificateFiles.PemCertificatePath,
            TlsPemPrivateKeyPath = tlsCertificateFiles.PemPrivateKeyPath,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1)));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port, encrypt: "True"));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var value = await command.ExecuteScalarAsync();

        Assert.Equal(1, Convert.ToInt32(value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SqlClient_EncryptOptional_AndEncryptTrue_WorkOnSameEndpoint()
    {
        using var tlsCertificateFiles = CreateTlsCertificateFiles();

        var options = new TdsServerOptions
        {
            TlsPfxPath = tlsCertificateFiles.PfxPath,
            TlsPfxPassword = tlsCertificateFiles.PfxPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1)));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        var optionalResult = await ExecuteScalarAsync(port, encrypt: "Optional");
        var encryptedResult = await ExecuteScalarAsync(port, encrypt: "True");

        Assert.Equal(1, optionalResult);
        Assert.Equal(1, encryptedResult);
    }

    [Fact]
    public async Task SqlClient_EncryptTrue_WhenServerDoesNotSupportEncryption_Throws()
    {
        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port, encrypt: "True"));
        _ = await Assert.ThrowsAnyAsync<SqlException>(() => connection.OpenAsync());
    }

    [Fact]
    public async Task SqlClient_QueryResult_CoversAllColumnTypes()
    {
        const string marker = "AllDataTypesMarker";
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (resultSet, expectedValues) = CreateResultSetWithAllDataTypes();

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.CommandText?.Contains(marker, StringComparison.Ordinal) == true)
                {
                    queryContextTask.TrySetResult(context);
                    var result = new TdsQueryResult();
                    result.ResultSets.Add(resultSet);
                    return ValueTask.FromResult(result);
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 /* {marker} */";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        for (var i = 0; i < expectedValues.Count; i++)
        {
            Assert.Equal(resultSet.Columns[i].Name, reader.GetName(i));
            var actualValue = reader.GetValue(i);
            var expectedValue = expectedValues[i];
            if (expectedValue is byte[] expectedBytes)
            {
                Assert.Equal(expectedBytes, Assert.IsType<byte[]>(actualValue));
            }
            else
            {
                Assert.Equal(expectedValue, actualValue);
            }
        }

        Assert.False(await reader.ReadAsync());
        _ = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static (TdsResultSet ResultSet, List<object> ExpectedValues) CreateResultSetWithAllDataTypes()
    {
        var resultSet = new TdsResultSet();
        var row = new List<object>();
        var expectedValues = new List<object>();

        foreach (var columnType in Enum.GetValues<TdsColumnType>())
        {
            var value = GetValue(columnType);
            resultSet.Columns.Add(new TdsColumn(columnType.ToString(), columnType, isNullable: false));
            row.Add(value);
            expectedValues.Add(GetExpectedSqlClientValue(columnType, value));
        }

        resultSet.Rows.Add(row);
        return (resultSet, expectedValues);
    }

    private static object GetValue(TdsColumnType columnType)
    {
        return columnType switch
        {
            TdsColumnType.TinyInt => (byte)1,
            TdsColumnType.SmallInt => (short)2,
            TdsColumnType.Int32 => 3,
            TdsColumnType.Int64 => 4L,
            TdsColumnType.Boolean => true,
            TdsColumnType.Real => 1.25f,
            TdsColumnType.Double => 2.5d,
            TdsColumnType.Decimal => 123.45m,
            TdsColumnType.Money => 987.65m,
            TdsColumnType.SmallMoney => 54.32m,
            TdsColumnType.NVarChar => "nvarchar",
            TdsColumnType.Binary => new byte[] { 0x10, 0x20, 0x30 },
            TdsColumnType.Guid => Guid.Parse("9f89d58d-f350-4ad6-af79-b2cbf2f65fd2", CultureInfo.InvariantCulture),
            TdsColumnType.Date => new DateOnly(2024, 05, 01),
            TdsColumnType.Time => new TimeSpan(1, 2, 3),
            TdsColumnType.DateTime => new DateTime(2024, 05, 01, 12, 34, 56, DateTimeKind.Utc),
            TdsColumnType.DateTime2 => new DateTime(2024, 05, 01, 12, 34, 56, DateTimeKind.Utc),
            TdsColumnType.DateTimeOffset => new DateTimeOffset(2024, 05, 01, 12, 34, 56, TimeSpan.FromHours(2)),
            TdsColumnType.Xml => "<root>xml</root>",
            TdsColumnType.Json => "{\"value\":42}",
            TdsColumnType.Variant => "variant",
            TdsColumnType.UserDefined => "userdefined",
            TdsColumnType.Table => "table",
            _ => throw new InvalidOperationException($"Unknown column type {columnType}"),
        };
    }

    private static object GetExpectedSqlClientValue(TdsColumnType columnType, object value)
    {
        return columnType switch
        {
            TdsColumnType.TinyInt => (byte)1,
            TdsColumnType.SmallInt => (short)2,
            TdsColumnType.Int32 => 3,
            TdsColumnType.Int64 => 4L,
            TdsColumnType.Boolean => true,
            TdsColumnType.Real => 1.25f,
            TdsColumnType.Double => 2.5d,
            TdsColumnType.Binary => (byte[])value,
            TdsColumnType.Date => "2024-05-01",
            TdsColumnType.Time => "01:02:03",
            TdsColumnType.DateTime => "2024-05-01T12:34:56.0000000Z",
            TdsColumnType.DateTime2 => "2024-05-01T12:34:56.0000000Z",
            TdsColumnType.DateTimeOffset => "2024-05-01T12:34:56.0000000+02:00",
            TdsColumnType.Guid => "9f89d58d-f350-4ad6-af79-b2cbf2f65fd2",
            TdsColumnType.Json => "{\"value\":42}",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static bool HasIntParameter(IReadOnlyList<TdsQueryParameter> parameters, int expectedValue)
    {
        return parameters.Any(parameter => IsExpectedIntParameter(parameter.Value, expectedValue));
    }

    private static bool IsExpectedIntParameter(TdsParameterValue value, int expectedValue)
    {
        return value.RawValue switch
        {
            byte typedValue => typedValue == expectedValue,
            short typedValue => typedValue == expectedValue,
            int typedValue => typedValue == expectedValue,
            long typedValue => typedValue == expectedValue,
            _ => false,
        };
    }

    private static TdsQueryResult CreateScalarResultSet(TdsColumnType columnType, object value)
    {
        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Value", columnType, isNullable: false));
        resultSet.Rows.Add([value]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);
        return result;
    }

    private static async Task<int> ExecuteScalarAsync(int port, string encrypt)
    {
        await using var connection = new SqlConnection(CreateConnectionString(port, encrypt: encrypt));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string CreateConnectionString(int port, string userName = "sa", string password = "Password123!", string encrypt = "Optional", bool trustServerCertificate = true)
    {
        return $"Server={IPAddress.Loopback},{port};User ID={userName};Password={password};Database=master;Encrypt={encrypt};TrustServerCertificate={(trustServerCertificate ? "True" : "False")};Pooling=False;Connect Timeout=5";
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
        const string pfxPassword = "Password123!";

        var pfxPath = Path.Combine(directoryPath, "server.pfx");
        File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, pfxPassword));

        var pemCertificatePath = Path.Combine(directoryPath, "server.crt.pem");
        File.WriteAllText(pemCertificatePath, certificate.ExportCertificatePem());

        var pemPrivateKeyPath = Path.Combine(directoryPath, "server.key.pem");
        File.WriteAllText(pemPrivateKeyPath, rsa.ExportPkcs8PrivateKeyPem());

        return new TlsCertificateFiles(directoryPath, pfxPath, pfxPassword, pemCertificatePath, pemPrivateKeyPath);
    }

    private sealed class TlsCertificateFiles : IDisposable
    {
        private readonly string _directoryPath;

        public TlsCertificateFiles(string directoryPath, string pfxPath, string pfxPassword, string pemCertificatePath, string pemPrivateKeyPath)
        {
            _directoryPath = directoryPath;
            PfxPath = pfxPath;
            PfxPassword = pfxPassword;
            PemCertificatePath = pemCertificatePath;
            PemPrivateKeyPath = pemPrivateKeyPath;
        }

        public string PfxPath { get; }

        public string PfxPassword { get; }

        public string PemCertificatePath { get; }

        public string PemPrivateKeyPath { get; }

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
