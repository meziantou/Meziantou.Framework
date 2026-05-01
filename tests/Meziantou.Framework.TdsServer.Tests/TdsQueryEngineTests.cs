using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Meziantou.Framework.Tds;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.QueryEngine;
using Microsoft.Data.SqlClient;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tds.Tests;

[RunIf(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
public sealed class TdsQueryEngineTests
{
    [Fact]
    public async Task SqlClient_QueryEngine_SelectColumns_ReturnsProjectedColumns()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = "SELECT Id, Name FROM customers";
            },
            """
            Id Name
            1 Alice
            2 Bob
            4 David
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_SelectStar_ReturnsAllColumns()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = "SELECT * FROM customers";
            },
            """
            Id Name
            1 Alice
            2 Bob
            4 David
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Where_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = "SELECT Id FROM customers WHERE Id > 1";
            },
            """
            Id
            2
            4
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_OrderBy_ReturnsOrderedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = "SELECT Id FROM customers ORDER BY Name DESC";
            },
            """
            Id
            4
            2
            1
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_InnerJoin_ReturnsProjectedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText =
                """
                SELECT c1.Id
                FROM customers c1
                INNER JOIN customers c2 ON c1.Id = c2.Id
                """;
            },
            """
            Id
            1
            2
            4
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_GroupBy_ReturnsGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = "SELECT Region FROM orders GROUP BY Region";
            },
            """
            Region
            North
            South
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Having_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = "SELECT Region, COUNT(*) AS Count FROM orders GROUP BY Region HAVING COUNT(*) > 1";
            },
            """
            Region Count
            North 2
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Parameters_UsesSpExecuteSqlParameters()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = "SELECT Id FROM customers WHERE Id > @id";
                _ = command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = 1 });
            },
            """
            Id
            2
            4
            """,
            expectedMaterializationCount: 1);
    }

    [Fact]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The stored procedure name is generated within the test and not user-controlled.")]
    public async Task SqlClient_QueryEngine_StoredProcedure_MapsParametersToDelegate()
    {
        var procedureName = "query_engine_proc_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var queryEngineOptions = CreateQueryEngineOptions();
        queryEngineOptions.StoredProcedures.Add(procedureName, (int id) => GetCustomers().Where(customer => customer.Id == id));

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = procedureName;
                _ = command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = 2 });
            },
            """
            Id Name
            2 Bob
            """,
            expectedMaterializationCount: 0);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_InvalidQuery_ReturnsServerError()
    {
        var invalidQueryTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryEngineOptions = CreateQueryEngineOptions();

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            async (context, cancellationToken) =>
            {
                var result = await TdsQueryEngine.CreateQueryHandler(queryEngineOptions)(context, cancellationToken);
                invalidQueryTask.TrySetResult(result.Error is not null);
                return result;
            });

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM customers WHERE Id = ";

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteReaderAsync());
        Assert.Equal(50004, exception.Number);
        Assert.True(await invalidQueryTask.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static TdsQueryEngineOptions CreateQueryEngineOptions()
    {
        var options = new TdsQueryEngineOptions();
        options.AddQueryRoot("customers", GetCustomers());
        options.AddQueryRoot("orders", GetOrders());
        return options;
    }

    private static Customer[] GetCustomers()
    {
        return
        [
            new Customer(1, "Alice"),
            new Customer(2, "Bob"),
            new Customer(4, "David"),
        ];
    }

    private static Order[] GetOrders()
    {
        return
        [
            new Order(1, "North"),
            new Order(2, "North"),
            new Order(3, "South"),
        ];
    }

    private static async Task ExecuteQuery(TdsQueryEngineOptions queryEngineOptions, Action<SqlCommand> configureCommand, string expected, int expectedMaterializationCount)
    {
        var materializationCount = 0;
        var materializeAsync = queryEngineOptions.MaterializeAsync;
        queryEngineOptions.MaterializeAsync = async (query, cancellationToken) =>
        {
            materializationCount++;
            return await materializeAsync(query, cancellationToken);
        };

        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            TdsQueryEngine.CreateQueryHandler(queryEngineOptions));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        await using var connection = new SqlConnection(CreateConnectionString(port));
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        configureCommand(command);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.Equal(NormalizeMultilineString(expected), await ReadResultAsync(reader));
        Assert.Equal(expectedMaterializationCount, materializationCount);
    }

    private static async Task<string> ReadResultAsync(SqlDataReader reader)
    {
        var lines = new List<string>();
        if (reader.FieldCount > 0)
        {
            lines.Add(string.Join(' ', Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
        }

        while (await reader.ReadAsync())
        {
            var values = Enumerable.Range(0, reader.FieldCount)
                .Select(index => FormatValue(reader.GetValue(index)));
            lines.Add(string.Join(' ', values));
        }

        return string.Join('\n', lines);
    }

    private static string FormatValue(object? value)
    {
        return value is null or DBNull ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string NormalizeMultilineString(string value)
    {
        return value.ReplaceLineEndings("\n").Trim();
    }

    private static string CreateConnectionString(int port, string userName = "sa", string password = "Password123!", string encrypt = "Optional", bool trustServerCertificate = true)
    {
        return $"Server={IPAddress.Loopback},{port};User ID={userName};Password={password};Database=master;Encrypt={encrypt};TrustServerCertificate={(trustServerCertificate ? "True" : "False")};Pooling=False;Connect Timeout=5";
    }

    private sealed record Customer(int Id, string Name);

    private sealed record Order(int Id, string Region);
}
