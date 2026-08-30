using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Meziantou.Framework.Tds.Handler;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using Meziantou.Xunit;
using SqlParser = Microsoft.SqlServer.Management.SqlParser.Parser.Parser;
using SqlParserParseOptions = Microsoft.SqlServer.Management.SqlParser.Parser.ParseOptions;
using SqlParserParseResult = Microsoft.SqlServer.Management.SqlParser.Parser.ParseResult;

namespace Meziantou.Framework.Tds.Tests;

[RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
public sealed class TdsServerProtocolTests
{
    [Fact]
    public void TdsQueryParameter_AsJson_ReturnsJsonObject()
    {
        var parameter = new TdsQueryParameter
        {
            Name = "@p",
            Value = "{\"value\":42}",
            Type = TdsColumnType.Json,
        };

        var json = parameter.AsJson();

        Assert.NotNull(json);
        Assert.Equal(42, json!["value"]!.GetValue<int>());
    }

    [Fact]
    public void TdsQueryParameter_AsXml_WithADtd_Throws()
    {
        // The value comes straight off the wire. XDocument.Parse would expand internal entities up to ten
        // million characters, so a handler calling AsXml on a small parameter could allocate megabytes.
        var parameter = new TdsQueryParameter
        {
            Name = "@xml",
            Value = """<?xml version="1.0"?><!DOCTYPE root [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]><root>&b;</root>""",
            Type = TdsColumnType.NVarChar,
        };

        _ = Assert.Throws<XmlException>(parameter.AsXml);
    }

    [Fact]
    public void TdsQueryParameter_AsXml_WithoutADtd_ReturnsTheDocument()
    {
        var parameter = new TdsQueryParameter
        {
            Name = "@xml",
            Value = """<root><item id="1">Alpha</item></root>""",
            Type = TdsColumnType.NVarChar,
        };

        var document = parameter.AsXml();

        Assert.Equal("Alpha", document?.Root?.Element("item")?.Value);
    }

    [Fact]
    public void TdsQueryParameter_Constructor_WithType_SetsType()
    {
        var parameter = new TdsQueryParameter
        {
            Name = "@p",
            Value = 42,
            Type = TdsColumnType.Int32,
        };

        Assert.Equal(TdsColumnType.Int32, parameter.Type);
    }

    [Fact]
    public void TdsQueryParameter_DbNull_IsHandledAsNull()
    {
        var parameter = new TdsQueryParameter
        {
            Name = "@p",
            Value = DBNull.Value,
            Type = TdsColumnType.NVarChar,
        };

        Assert.True(parameter.IsNull);
        Assert.Null(parameter.AsString());
        Assert.Null(parameter.AsInt32());
        Assert.Null(parameter.AsJson());
    }

    [Fact]
    public async Task SqlClient_AuthenticationCallback_ReceivesCredentials()
    {
        const string UserName = "sa";
        const string Password = "Password123!";
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

        await using var connection = new SqlConnection(CreateConnectionString(port, UserName, Password));
        await connection.OpenAsync();

        var capturedContext = await authenticationContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(UserName, capturedContext.UserName);
        Assert.NotNull(capturedContext.Password);
        Assert.Equal("master", capturedContext.Database);
    }

    [Fact]
    public async Task SqlClient_TextQuery_WithoutParameters_UsesSqlBatch()
    {
        const string Marker = "TextQueryWithoutParametersMarker";
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
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
        command.CommandText = $"SELECT 1 /* {Marker} */";

        var result = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(123, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(TdsQueryRequestType.SqlBatch, capturedContext.RequestType);
        Assert.Equal($"SELECT 1 /* {Marker} */", capturedContext.CommandText);
    }

    [Fact]
    public async Task SqlClient_TextQuery_CommandText_ExcludesAllHeaders()
    {
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                queryContextTask.TrySetResult(context);
                return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1));
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        _ = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The SQLBatch payload starts with an ALL_HEADERS block whose bytes decode to control
        // characters. It must not be exposed through CommandText.
        Assert.Equal("SELECT 1", capturedContext.CommandText);
        Assert.DoesNotContain(capturedContext.CommandText, character => char.IsControl(character));
    }

    [Fact]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The SQL query is generated within the test and not user-controlled.")]
    public async Task SqlClient_TextQuery_LargePayload_UsesMultiplePackets()
    {
        const string Marker = "LargePayloadMarker";
        var longComment = new string('a', 7000);
        var query = $"SELECT 1 /* {Marker}{longComment} */";
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 456));
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await WaitForServerReadyAsync(port, TimeSpan.FromSeconds(30));

        await using var connection = new SqlConnection(CreateConnectionString(port, connectTimeout: 30) + ";Packet Size=512");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = query;

        var result = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(456, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(TdsQueryRequestType.SqlBatch, capturedContext.RequestType);
        Assert.Equal(query, capturedContext.CommandText);
    }

    [Fact]
    public async Task SqlClient_TextQuery_UserContext_FromAuthentication_IsAvailableInQueryContext()
    {
        const string Marker = "TextQueryUserContextMarker";
        const string UserId = "42";
        var queryUserIdTask = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master", CreateUserContext(UserId))),
            (context, cancellationToken) =>
            {
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
                {
                    queryUserIdTask.TrySetResult(context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier));
                    return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1));
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 /* {Marker} */";

        var result = await command.ExecuteScalarAsync();
        var capturedUserId = await queryUserIdTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(UserId, capturedUserId);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(4000)]
    [InlineData(4001)]
    [InlineData(100000)]
    public async Task SqlClient_ResultSet_NVarCharValue_IsNotTruncated(int length)
    {
        var expected = string.Create(length, length, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = (char)('a' + (i % 26));
            }
        });

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Value", TdsColumnType.NVarChar));
        resultSet.Rows.Add([expected]);
        resultSet.Rows.Add([null]);

        var rows = await ReadStringColumnAsync(resultSet);

        Assert.Equal([expected, null], rows);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(8000)]
    [InlineData(100000)]
    public async Task SqlClient_ResultSet_BinaryValue_IsNotTruncated(int length)
    {
        var expected = new byte[length];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = (byte)(i % 256);
        }

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Value", TdsColumnType.Binary));
        resultSet.Rows.Add([expected]);
        resultSet.Rows.Add([null]);

        var rows = await ReadResultSetAsync(resultSet, (reader, ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToHexString((byte[])reader.GetValue(ordinal)));

        Assert.Equal([Convert.ToHexString(expected), null], rows);
    }

    [Fact]
    public async Task SqlClient_ResultSet_MixedLengths_UseIndependentColumnFraming()
    {
        var longValue = new string('x', 9000);

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Short", TdsColumnType.NVarChar));
        resultSet.Columns.Add(new TdsColumn("Long", TdsColumnType.NVarChar));
        resultSet.Rows.Add(["abc", longValue]);
        resultSet.Rows.Add([null, "def"]);

        var rows = await ReadResultSetAsync(resultSet, (reader, _) => (reader.IsDBNull(0) ? null : reader.GetString(0)) + "|" + reader.GetString(1));

        Assert.Equal(["abc|" + longValue, "|def"], rows);
    }

    [Fact]
    public async Task SqlClient_ResultSet_ValueThatCannotBeSerialized_ReturnsErrorInsteadOfDroppingTheConnection()
    {
        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Value", TdsColumnType.Int32));
        resultSet.Rows.Add(["not-a-number"]);

        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => ValueTask.FromResult(result));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteScalarAsync());

        // A SQL error, not "A transport-level error has occurred", which retry logic treats as transient.
        Assert.Equal(50005, exception.Number);
        Assert.Contains("Failed to build the query response", exception.Message);
    }

    [Fact]
    public async Task SqlClient_ResultSet_TypedColumns_UseTheirOwnTdsTypes()
    {
        var expectedGuid = Guid.Parse("1b4e28ba-2fa1-11d2-883f-0016d3cca427");
        var expectedDateTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var expectedDateTime2 = new DateTime(2020, 1, 2, 3, 4, 5, 123, DateTimeKind.Unspecified);
        var expectedOffset = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));

        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Decimal", TdsColumnType.Decimal));
        resultSet.Columns.Add(new TdsColumn("Money", TdsColumnType.Money));
        resultSet.Columns.Add(new TdsColumn("SmallMoney", TdsColumnType.SmallMoney));
        resultSet.Columns.Add(new TdsColumn("Guid", TdsColumnType.Guid));
        resultSet.Columns.Add(new TdsColumn("Date", TdsColumnType.Date));
        resultSet.Columns.Add(new TdsColumn("Time", TdsColumnType.Time));
        resultSet.Columns.Add(new TdsColumn("DateTime", TdsColumnType.DateTime));
        resultSet.Columns.Add(new TdsColumn("DateTime2", TdsColumnType.DateTime2));
        resultSet.Columns.Add(new TdsColumn("DateTimeOffset", TdsColumnType.DateTimeOffset));
        resultSet.Columns.Add(new TdsColumn("Xml", TdsColumnType.Xml));
        resultSet.Rows.Add(
        [
            1234.5678m,
            12.34m,
            -1.5m,
            expectedGuid,
            new DateOnly(2020, 1, 2),
            new TimeOnly(3, 4, 5, 123),
            expectedDateTime,
            expectedDateTime2,
            expectedOffset,
            "<root />",
        ]);

        var values = await ReadRowAsync(resultSet);

        // Each value comes back as its CLR type, not as a string.
        Assert.Equal(1234.5678m, values[0]);
        Assert.Equal(12.34m, values[1]);
        Assert.Equal(-1.5m, values[2]);
        Assert.Equal(expectedGuid, values[3]);
        Assert.Equal(new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Unspecified), values[4]);
        Assert.Equal(new TimeSpan(0, 3, 4, 5, 123), values[5]);
        Assert.Equal(expectedDateTime, values[6]);
        Assert.Equal(expectedDateTime2, values[7]);
        Assert.Equal(expectedOffset, values[8]);
        Assert.Equal("<root />", values[9]);
    }

    [Fact]
    public async Task SqlClient_ResultSet_TypedColumns_NullValues()
    {
        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Decimal", TdsColumnType.Decimal));
        resultSet.Columns.Add(new TdsColumn("Money", TdsColumnType.Money));
        resultSet.Columns.Add(new TdsColumn("Guid", TdsColumnType.Guid));
        resultSet.Columns.Add(new TdsColumn("Date", TdsColumnType.Date));
        resultSet.Columns.Add(new TdsColumn("Time", TdsColumnType.Time));
        resultSet.Columns.Add(new TdsColumn("DateTime", TdsColumnType.DateTime));
        resultSet.Columns.Add(new TdsColumn("DateTime2", TdsColumnType.DateTime2));
        resultSet.Columns.Add(new TdsColumn("DateTimeOffset", TdsColumnType.DateTimeOffset));
        resultSet.Columns.Add(new TdsColumn("Xml", TdsColumnType.Xml));
        resultSet.Rows.Add([null, null, null, null, null, null, null, null, null]);

        var values = await ReadRowAsync(resultSet);

        Assert.All(values, value => Assert.Equal(DBNull.Value, value));
    }

    [Fact]
    public async Task SqlClient_ResultSet_DecimalColumn_UsesLargestScaleInTheColumn()
    {
        var resultSet = new TdsResultSet();
        resultSet.Columns.Add(new TdsColumn("Value", TdsColumnType.Decimal));
        resultSet.Rows.Add([1m]);
        resultSet.Rows.Add([2.5m]);
        resultSet.Rows.Add([-3.12345m]);

        var rows = await ReadResultSetAsync(resultSet, (reader, ordinal) => reader.GetDecimal(ordinal).ToString(CultureInfo.InvariantCulture));

        Assert.Equal(["1.00000", "2.50000", "-3.12345"], rows);
    }

    private static async Task<object[]> ReadRowAsync(TdsResultSet resultSet)
    {
        var rows = await ReadResultSetAsync(resultSet, (reader, _) =>
        {
            var values = new object[reader.FieldCount];
            _ = reader.GetValues(values);
            return values;
        });

        return Assert.Single(rows);
    }

    private static Task<List<string?>> ReadStringColumnAsync(TdsResultSet resultSet)
    {
        return ReadResultSetAsync(resultSet, (reader, ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal));
    }

    private static async Task<List<T>> ReadResultSetAsync<T>(TdsResultSet resultSet, Func<SqlDataReader, int, T> readValue)
    {
        var result = new TdsQueryResult();
        result.ResultSets.Add(resultSet);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => ValueTask.FromResult(result));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(readValue(reader, 0));
        }

        return rows;
    }

    [Fact]
    public async Task SqlClient_TextQuery_WithParameters_UsesRpc()
    {
        const string Marker = "TextQueryWithParametersMarker";
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
                    context.Parameters.Any(parameter => parameter.AsString()?.Contains(Marker, StringComparison.Ordinal) == true) &&
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
        command.CommandText = $"SELECT @value /* {Marker} */";
        _ = command.Parameters.Add(new SqlParameter("@value", SqlDbType.Int) { Value = 42 });

        var result = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(42, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(TdsQueryRequestType.Rpc, capturedContext.RequestType);
        Assert.Equal("sp_executesql", capturedContext.ProcedureName, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(capturedContext.Parameters, parameter => parameter.Type == TdsColumnType.Int32 && IsExpectedIntParameter(parameter, 42));
    }

    [Fact]
    public async Task SqlClient_RpcParameter_VarBinaryMax_PreservesValue()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc)
                {
                    queryContextTask.TrySetResult(context);
                }

                return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1));
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @value";
        _ = command.Parameters.Add(new SqlParameter("@value", SqlDbType.VarBinary, -1) { Value = payload });

        _ = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var parameter = Assert.Single(capturedContext.Parameters, candidate => candidate.Name == "@value");
        Assert.Equal(TdsColumnType.Binary, parameter.Type);
        Assert.Equal(payload, parameter.AsBinary());
    }

    [Fact]
    public async Task SqlClient_RpcParameters_CommonSqlTypes_AreAllDecoded()
    {
        var expectedGuid = Guid.Parse("1b4e28ba-2fa1-11d2-883f-0016d3cca427");
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc)
                {
                    queryContextTask.TrySetResult(context);
                }

                return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1));
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @guid, @decimal, @money, @smallmoney, @datetime, @smalldatetime, @date, @time, @datetime2, @offset, @int";
        _ = command.Parameters.Add(new SqlParameter("@guid", SqlDbType.UniqueIdentifier) { Value = expectedGuid });
        _ = command.Parameters.Add(new SqlParameter("@decimal", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = 1234.5678m });
        _ = command.Parameters.Add(new SqlParameter("@money", SqlDbType.Money) { Value = 12.34m });
        _ = command.Parameters.Add(new SqlParameter("@smallmoney", SqlDbType.SmallMoney) { Value = -1.5m });
        _ = command.Parameters.Add(new SqlParameter("@datetime", SqlDbType.DateTime) { Value = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified) });
        _ = command.Parameters.Add(new SqlParameter("@smalldatetime", SqlDbType.SmallDateTime) { Value = new DateTime(2020, 1, 2, 3, 4, 0, DateTimeKind.Unspecified) });
        _ = command.Parameters.Add(new SqlParameter("@date", SqlDbType.Date) { Value = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Unspecified) });
        _ = command.Parameters.Add(new SqlParameter("@time", SqlDbType.Time) { Value = new TimeSpan(0, 3, 4, 5, 123) });
        _ = command.Parameters.Add(new SqlParameter("@datetime2", SqlDbType.DateTime2) { Value = new DateTime(2020, 1, 2, 3, 4, 5, 123, DateTimeKind.Unspecified) });
        _ = command.Parameters.Add(new SqlParameter("@offset", SqlDbType.DateTimeOffset) { Value = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)) });
        _ = command.Parameters.Add(new SqlParameter("@int", SqlDbType.Int) { Value = 42 });

        _ = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(capturedContext.HasCompleteParameters);
        Assert.Equal(expectedGuid, GetParameterValue(capturedContext, "@guid", TdsColumnType.Guid));
        Assert.Equal(1234.5678m, GetParameterValue(capturedContext, "@decimal", TdsColumnType.Decimal));
        Assert.Equal(12.34m, GetParameterValue(capturedContext, "@money", TdsColumnType.Money));
        Assert.Equal(-1.5m, GetParameterValue(capturedContext, "@smallmoney", TdsColumnType.SmallMoney));
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), GetParameterValue(capturedContext, "@datetime", TdsColumnType.DateTime));
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 0, DateTimeKind.Unspecified), GetParameterValue(capturedContext, "@smalldatetime", TdsColumnType.DateTime));
        Assert.Equal(new DateOnly(2020, 1, 2), GetParameterValue(capturedContext, "@date", TdsColumnType.Date));
        Assert.Equal(new TimeOnly(3, 4, 5, 123), GetParameterValue(capturedContext, "@time", TdsColumnType.Time));
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, 123, DateTimeKind.Unspecified), GetParameterValue(capturedContext, "@datetime2", TdsColumnType.DateTime2));
        Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), GetParameterValue(capturedContext, "@offset", TdsColumnType.DateTimeOffset));

        // The int parameter is sent last: before this fix the first undecodable type dropped everything after it.
        Assert.Equal(42, GetParameterValue(capturedContext, "@int", TdsColumnType.Int32));
    }

    [Fact]
    public async Task SqlClient_RpcParameters_UndecodableType_ReportsIncompleteParameters()
    {
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc)
                {
                    queryContextTask.TrySetResult(context);
                }

                return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1));
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @xml, @int";
        _ = command.Parameters.Add(new SqlParameter("@xml", SqlDbType.Xml) { Value = "<root />" });
        _ = command.Parameters.Add(new SqlParameter("@int", SqlDbType.Int) { Value = 42 });

        _ = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(capturedContext.HasCompleteParameters);
        Assert.DoesNotContain(capturedContext.Parameters, parameter => parameter.Name == "@int");
    }

    private static object? GetParameterValue(TdsQueryContext context, string name, TdsColumnType expectedType)
    {
        var parameter = Assert.Single(context.Parameters, candidate => candidate.Name == name);
        Assert.Equal(expectedType, parameter.Type);
        return parameter.Value;
    }

    [Fact]
    public async Task SqlClient_RpcParameter_VarBinaryMax_Null_IsDecodedAsNull()
    {
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc)
                {
                    queryContextTask.TrySetResult(context);
                }

                return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1));
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @value";
        _ = command.Parameters.Add(new SqlParameter("@value", SqlDbType.VarBinary, -1) { Value = DBNull.Value });

        _ = await command.ExecuteScalarAsync();
        var capturedContext = await queryContextTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var parameter = Assert.Single(capturedContext.Parameters, candidate => candidate.Name == "@value");
        Assert.Equal(TdsColumnType.Binary, parameter.Type);
        Assert.True(parameter.IsNull);
    }

    [Fact]
    public async Task SqlClient_TextQuery_ParsedWithSqlParser_ReturnsFilteredCustomers()
    {
        const string Query = "SELECT Id, Name FROM customers WHERE Id = 1";
        var parseSucceededTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var customers = new[]
        {
            new Customer(1, "Alice"),
            new Customer(2, "Bob"),
            new Customer(3, "Charlie"),
        };

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (!TryParseCustomerQuery(context.CommandText, out var customerId))
                {
                    parseSucceededTask.TrySetResult(false);
                    return ValueTask.FromResult(TdsQueryResult.FromError(new TdsQueryError
                    {
                        Message = "Invalid query",
                    }));
                }

                parseSucceededTask.TrySetResult(true);
                var resultSet = new TdsResultSet();
                resultSet.Columns.Add(new TdsColumn("Id", TdsColumnType.Int32, isNullable: false));
                resultSet.Columns.Add(new TdsColumn("Name", TdsColumnType.NVarChar, isNullable: false));
                foreach (var customer in customers.Where(customer => customer.Id == customerId))
                {
                    resultSet.Rows.Add([customer.Id, customer.Name]);
                }

                var result = new TdsQueryResult();
                result.ResultSets.Add(resultSet);
                return ValueTask.FromResult(result);
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = Query;
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("Alice", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
        Assert.True(await parseSucceededTask.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SqlClient_TextQuery_InvalidQuery_ReturnsServerError()
    {
        const string Query = "SELECT Id, Name FROM customers WHERE Id = ";
        var invalidQueryTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (!TryParseCustomerQuery(context.CommandText, out _))
                {
                    invalidQueryTask.TrySetResult(true);
                    return ValueTask.FromResult(TdsQueryResult.FromError(new TdsQueryError
                    {
                        Number = 50001,
                        Message = "Invalid query",
                    }));
                }

                invalidQueryTask.TrySetResult(false);
                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = Query;

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteReaderAsync());
        Assert.Equal(50001, exception.Number);
        Assert.Contains("Invalid query", exception.Message);
        Assert.True(await invalidQueryTask.Task.WaitAsync(TimeSpan.FromSeconds(5)));
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
    public async Task SqlClient_StoredProcedure_UserContext_FromAuthentication_IsAvailableInQueryContext()
    {
        const string UserId = "42";
        var procedureName = "proc_user_context_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var queryUserIdTask = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master", CreateUserContext(UserId))),
            (context, cancellationToken) =>
            {
                if (context.RequestType == TdsQueryRequestType.Rpc &&
                    string.Equals(context.ProcedureName, procedureName, StringComparison.OrdinalIgnoreCase))
                {
                    queryUserIdTask.TrySetResult(context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier));
                    return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 1));
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

        var result = await command.ExecuteScalarAsync();
        var capturedUserId = await queryUserIdTask.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, Convert.ToInt32(result, CultureInfo.InvariantCulture));
        Assert.Equal(UserId, capturedUserId);
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
                    context.Parameters.Any(parameter => string.Equals(parameter.AsString(), "sample", StringComparison.Ordinal)))
                {
                    queryContextTask.TrySetResult(context);
                    return ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 7));
                }

                return ValueTask.FromResult(new TdsQueryResult());
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await WaitForServerReadyAsync(port, TimeSpan.FromSeconds(30));

        await using var connection = new SqlConnection(CreateConnectionString(port, connectTimeout: 30));
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
        Assert.Contains(capturedContext.Parameters, parameter => IsExpectedIntParameter(parameter, 7));
        Assert.Contains(capturedContext.Parameters, parameter => string.Equals(parameter.AsString(), "sample", StringComparison.Ordinal));
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

        var value = await ExecuteScalarWithTransientSqlRetryAsync(port, encrypt: "True");

        Assert.Equal(1, value);
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

        var value = await ExecuteScalarWithTransientSqlRetryAsync(port, encrypt: "True");

        Assert.Equal(1, value);
    }

    [Fact]
    public async Task SqlClient_EncryptOptional_DowngradesAfterLogin_AndKeepsServingQueries()
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
            (context, cancellationToken) => ValueTask.FromResult(CreateScalarResultSet(TdsColumnType.Int32, 7)));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        // Encrypt=Optional negotiates ENCRYPT_OFF: the login packet is encrypted and the session then
        // reverts to the raw transport. Several round trips confirm the swap left a usable connection.
        await using var connection = new SqlConnection(CreateConnectionString(port, encrypt: "Optional"));
        await connection.OpenAsync();

        for (var i = 0; i < 3; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            Assert.Equal(7, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
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
        var encryptedResult = await ExecuteScalarWithTransientSqlRetryAsync(port, encrypt: "True");

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
        const string Marker = "AllDataTypesMarker";
        var queryContextTask = new TaskCompletionSource<TdsQueryContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (resultSet, expectedValues) = CreateResultSetWithAllDataTypes();

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) =>
            {
                if (context.CommandText?.Contains(Marker, StringComparison.Ordinal) == true)
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
        command.CommandText = $"SELECT 1 /* {Marker} */";

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
            TdsColumnType.Decimal => 123.45m,
            TdsColumnType.Money => 987.65m,
            TdsColumnType.SmallMoney => 54.32m,
            TdsColumnType.Date => new DateTime(2024, 05, 01, 0, 0, 0, DateTimeKind.Unspecified),
            TdsColumnType.Time => new TimeSpan(1, 2, 3),
            TdsColumnType.DateTime => new DateTime(2024, 05, 01, 12, 34, 56, DateTimeKind.Unspecified),
            TdsColumnType.DateTime2 => new DateTime(2024, 05, 01, 12, 34, 56, DateTimeKind.Unspecified),
            TdsColumnType.DateTimeOffset => new DateTimeOffset(2024, 05, 01, 12, 34, 56, TimeSpan.FromHours(2)),
            TdsColumnType.Guid => (Guid)value,
            TdsColumnType.Xml => "<root>xml</root>",
            TdsColumnType.Json => "{\"value\":42}",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static bool HasIntParameter(IReadOnlyList<TdsQueryParameter> parameters, int expectedValue)
    {
        return parameters.Any(parameter => IsExpectedIntParameter(parameter, expectedValue));
    }

    private static bool IsExpectedIntParameter(TdsQueryParameter parameter, int expectedValue)
    {
        return parameter.Value switch
        {
            byte typedValue => typedValue == expectedValue,
            short typedValue => typedValue == expectedValue,
            int typedValue => typedValue == expectedValue,
            long typedValue => typedValue == expectedValue,
            _ => false,
        };
    }

    private static bool TryParseCustomerQuery(string? query, out int customerId)
    {
        customerId = default;
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var parseResult = SqlParser.Parse(query, new SqlParserParseOptions(), out _);
        if (HasSqlParserErrors(parseResult))
        {
            return false;
        }

        if (parseResult.Script.Batches.Count != 1)
        {
            return false;
        }

        if (parseResult.Script.Batches[0].Statements.Count != 1 ||
            parseResult.Script.Batches[0].Statements[0] is not SqlSelectStatement selectStatement ||
            selectStatement.SelectSpecification.QueryExpression is not SqlQuerySpecification querySpecification)
        {
            return false;
        }

        if (querySpecification.SelectClause.SelectExpressions.Count != 2)
        {
            return false;
        }

        if (querySpecification.SelectClause.SelectExpressions[0] is not SqlSelectScalarExpression { Expression: SqlColumnRefExpression firstSelectColumn })
        {
            return false;
        }

        if (querySpecification.SelectClause.SelectExpressions[1] is not SqlSelectScalarExpression { Expression: SqlColumnRefExpression secondSelectColumn })
        {
            return false;
        }

        if (!string.Equals(firstSelectColumn.ColumnName.Value, "Id", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(secondSelectColumn.ColumnName.Value, "Name", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (querySpecification.FromClause is null ||
            querySpecification.FromClause.TableExpressions.Count != 1 ||
            querySpecification.FromClause.TableExpressions[0] is not SqlTableRefExpression tableExpression ||
            !string.Equals(tableExpression.ObjectIdentifier.ObjectName.Value, "customers", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (querySpecification.WhereClause?.Expression is not SqlComparisonBooleanExpression
            {
                ComparisonOperator: SqlComparisonBooleanExpressionType.Equals,
                Left: SqlColumnRefExpression predicateColumn,
                Right: SqlLiteralExpression predicateValue,
            } ||
            !string.Equals(predicateColumn.ColumnName.Value, "Id", StringComparison.OrdinalIgnoreCase) ||
            predicateValue.Type != LiteralValueType.Integer)
        {
            return false;
        }

        return int.TryParse(predicateValue.Value, NumberStyles.None, CultureInfo.InvariantCulture, out customerId);
    }

    private static bool HasSqlParserErrors(SqlParserParseResult parseResult)
    {
        return parseResult.Errors.Any() || parseResult.ParseErrors.Any();
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

    private static ClaimsPrincipal CreateUserContext(string userId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "test-user"),
        ],
        authenticationType: "Test");

        return new ClaimsPrincipal(identity);
    }

    private static async Task<int> ExecuteScalarAsync(int port, string encrypt, int connectTimeout = 5)
    {
        await using var connection = new SqlConnection(CreateConnectionString(port, encrypt: encrypt, connectTimeout: connectTimeout));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static Task<int> ExecuteScalarWithTransientSqlRetryAsync(int port, string encrypt, int connectTimeout = 15)
    {
        return ExecuteWithTransientSqlRetry(() => ExecuteScalarAsync(port, encrypt: encrypt, connectTimeout: connectTimeout));
    }

    private static async Task<T> ExecuteWithTransientSqlRetry<T>(Func<Task<T>> action)
    {
        const int MaxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (SqlException ex) when (attempt < MaxAttempts && IsTransientSqlOpenFailure(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }

    private static bool IsTransientSqlOpenFailure(SqlException exception)
    {
        if (exception.Number != -2)
        {
            return false;
        }

        var hasTimeout = exception.Message.Contains("Connection Timeout Expired", StringComparison.OrdinalIgnoreCase);
        var hasPreLogin = exception.Message.Contains("pre-login", StringComparison.OrdinalIgnoreCase);
        var hasPostLogin = exception.Message.Contains("post-login", StringComparison.OrdinalIgnoreCase);
        return hasTimeout && (hasPreLogin || hasPostLogin);
    }

    private static async Task WaitForServerReadyAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            using var client = new TcpClient();

            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationTokenSource.Token);
                return;
            }
            catch (OperationCanceledException ex)
            {
                lastException = ex;
            }
            catch (SocketException ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException($"The test server on {IPAddress.Loopback}:{port} was not ready within {timeout}.", lastException);
    }

    [Fact]
    public async Task TdsServer_StartAsync_WhenAListenerCannotBind_RollsBackTheOthers()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        var occupiedPort = ((IPEndPoint)occupied.LocalEndpoint).Port;

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);
        options.AddTcpListener(occupiedPort, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));

        _ = await Assert.ThrowsAsync<SocketException>(() => server.StartAsync());

        // The listener that did bind is released, so the server is not half-started.
        Assert.Empty(server.Ports);

        occupied.Stop();
        await server.StartAsync();
        Assert.Equal(2, server.Ports.Count);
    }

    [Fact]
    public async Task TdsServer_Logger_ReceivesQueryHandlerFailures()
    {
        var logger = new CollectingLogger();

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => throw new InvalidOperationException("boom"),
            logger);

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteScalarAsync());

        Assert.Contains(logger.Entries, entry => entry.Contains("Unhandled exception in query handler", StringComparison.Ordinal));
    }

    private sealed class CollectingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(formatter(state, exception));
        }
    }

    [Fact]
    public async Task TdsServer_WithoutTlsCertificate_LogsAWarning()
    {
        var logger = new CollectingLogger();

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()),
            logger);

        await server.StartAsync();

        Assert.Contains(logger.Entries, entry => entry.Contains("No TLS certificate is configured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TdsServer_WithTlsCertificate_DoesNotLogAWarning()
    {
        using var tlsCertificateFiles = CreateTlsCertificateFiles();
        var logger = new CollectingLogger();

        var options = new TdsServerOptions
        {
            TlsPfxPath = tlsCertificateFiles.PfxPath,
            TlsPfxPassword = tlsCertificateFiles.PfxPassword,
        };
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()),
            logger);

        await server.StartAsync();

        Assert.DoesNotContain(logger.Entries, entry => entry.Contains("No TLS certificate is configured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TdsServer_Dispose_IsIdempotent()
    {
        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));

        await server.StartAsync();
        server.Dispose();

        Assert.Null(Record.Exception(server.Dispose));
    }

    [Fact]
    public async Task TdsServer_StartAsync_AfterDispose_Throws()
    {
        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));

        await server.StartAsync();
        server.Dispose();

        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => server.StartAsync());
    }

    [Fact]
    public void TdsServer_Dispose_WithoutStart_DoesNotThrow()
    {
        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success()),
            (context, cancellationToken) => ValueTask.FromResult(new TdsQueryResult()));

        Assert.Null(Record.Exception(server.Dispose));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(511)]
    [InlineData(65536)]
    [InlineData(100000)]
    public void TdsServerOptions_PacketSize_OutsideTheSupportedRange_Throws(int packetSize)
    {
        var options = new TdsServerOptions();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => options.PacketSize = packetSize);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    [InlineData(65535)]
    public void TdsServerOptions_PacketSize_InsideTheSupportedRange_IsAccepted(int packetSize)
    {
        var options = new TdsServerOptions
        {
            PacketSize = packetSize,
        };

        Assert.Equal(packetSize, options.PacketSize);
    }

    [Fact]
    public async Task SqlClient_QueryError_WithAVeryLongMessage_IsTruncatedInsteadOfOverflowingTheToken()
    {
        var result = TdsQueryResult.FromError(new TdsQueryError
        {
            Number = 50004,
            State = 1,
            Class = 16,
            Message = new string('e', 100_000),
        });

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            (context, cancellationToken) => ValueTask.FromResult(result));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteScalarAsync());

        Assert.Equal(50004, exception.Number);

        // The message fits in the token's 16-bit length field rather than wrapping it. SqlClient appends its
        // own note about the severity, so compare the first line only. Normalise the line endings first: they
        // are CRLF on Windows and LF elsewhere.
        var reportedMessage = exception.Message.ReplaceLineEndings("\n").Split('\n')[0];
        Assert.Equal(new string('e', 32_000), reportedMessage);
    }

    private static string CreateConnectionString(int port, string userName = "sa", string password = "Password123!", string encrypt = "Optional", bool trustServerCertificate = true, int connectTimeout = 5)
    {
        return $"Server={IPAddress.Loopback},{port};User ID={userName};Password={password};Database=master;Encrypt={encrypt};TrustServerCertificate={(trustServerCertificate ? "True" : "False")};Pooling=False;Connect Timeout={connectTimeout}";
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

        var pemCertificatePath = Path.Combine(directoryPath, "server.crt.pem");
        File.WriteAllText(pemCertificatePath, certificate.ExportCertificatePem());

        var pemPrivateKeyPath = Path.Combine(directoryPath, "server.key.pem");
        File.WriteAllText(pemPrivateKeyPath, rsa.ExportPkcs8PrivateKeyPem());

        return new TlsCertificateFiles(directoryPath, pfxPath, PfxPassword, pemCertificatePath, pemPrivateKeyPath);
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

    private sealed record Customer(int Id, string Name);

}
