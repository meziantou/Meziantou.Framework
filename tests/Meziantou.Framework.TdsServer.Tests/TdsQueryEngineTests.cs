using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
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
                command.CommandText = """
                    SELECT Id, Name
                    FROM customers
                    """;
            },
            """
            Id Name
            1 Alice
            2 Bob
            4 David
            """,
            expectedMaterializedQueries: "Customer[].Select(row => new TdsProjection() {Id = row.Id, Name = row.Name})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_SelectDistinct_ReturnsDistinctRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT DISTINCT Region
                    FROM orders
                    ORDER BY Region
                    """;
            },
            """
            Region
            North
            South
            """,
            expectedMaterializedQueries: "Order[].OrderBy(row => row.Region).Select(row => new TdsProjection() {Region = row.Region}).Distinct()");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Union_ReturnsDistinctRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id <= 2
                    UNION SELECT Id
                    FROM customers
                    WHERE Id >= 2
                    ORDER BY Id
                    """;
            },
            """
            Id
            1
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id <= 2)).Select(row => new TdsProjection() {Id = row.Id}).Union(Customer[].Where(row => (row.Id >= 2)).Select(row => new TdsProjection() {Id = row.Id})).OrderBy(row => row.Id)");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_SelectStar_ReturnsAllColumns()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT *
                    FROM customers
                    """;
            },
            """
            Id Name
            1 Alice
            2 Bob
            4 David
            """,
            expectedMaterializedQueries: "Customer[]");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Where_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id > 1
                    """;
            },
            """
            Id
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id > 1)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereEqualsOperator_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id = 2
                    """;
            },
            """
            Id
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id == 2)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereNotEqualsOperator_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id <> 2
                    """;
            },
            """
            Id
            1
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id != 2)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereGreaterThanOperator_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id > 2
                    """;
            },
            """
            Id
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id > 2)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereGreaterThanOrEqualOperator_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id >= 2
                    """;
            },
            """
            Id
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id >= 2)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereLessThanOperator_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id < 2
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id < 2)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereLessThanOrEqualOperator_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id <= 2
                    """;
            },
            """
            Id
            1
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id <= 2)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereAnd_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id > 1 AND Id < 4
                    """;
            },
            """
            Id
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(row => ((row.Id > 1) AndAlso (row.Id < 4))).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereOr_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id = 1 OR Id = 4
                    """;
            },
            """
            Id
            1
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => ((row.Id == 1) OrElse (row.Id == 4))).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereIsNull_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM nullable_customers
                    WHERE Name IS NULL
                    """;
            },
            """
            Id
            3
            """,
            expectedMaterializedQueries: "NullableCustomer[].Where(row => (row.Name == null)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereIsNotNull_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM nullable_customers
                    WHERE Name IS NOT NULL
                    """;
            },
            """
            Id
            1
            2
            """,
            expectedMaterializedQueries: "NullableCustomer[].Where(row => Not((row.Name == null))).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereInCollection_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id IN (1, 4)
                    """;
            },
            """
            Id
            1
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => new [] {1, 4}.Contains(row.Id)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereInSubquery_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id IN (SELECT Id
                    FROM orders)
                    """;
            },
            """
            Id
            1
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(row => Order[].Select(row => row.Id).Contains(row.Id)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereNotInSubquery_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id NOT IN (SELECT Id
                    FROM orders)
                    """;
            },
            """
            Id
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => Not(Order[].Select(row => row.Id).Contains(row.Id))).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Top_ReturnsFirstRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT TOP 2 Id
                    FROM customers
                    ORDER BY Id
                    """;
            },
            """
            Id
            1
            2
            """,
            expectedMaterializedQueries: "Customer[].OrderBy(row => row.Id).Select(row => new TdsProjection() {Id = row.Id}).Take(2)");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_OffsetFetch_ReturnsPagedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    ORDER BY Id
                    OFFSET 1 ROWS
                    FETCH NEXT 2 ROWS ONLY
                    """;
            },
            """
            Id
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].OrderBy(row => row.Id).Skip(1).Take(2).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_OrderBy_ReturnsOrderedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    ORDER BY Name DESC
                    """;
            },
            """
            Id
            4
            2
            1
            """,
            expectedMaterializedQueries: "Customer[].OrderByDescending(row => row.Name).Select(row => new TdsProjection() {Id = row.Id})");
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
            expectedMaterializedQueries: "Customer[].Join(Customer[], left => left.Id, right => right.Id, (left, right) => new TdsCarrier() {c1 = left, c2 = right}).Select(row => new TdsProjection() {Id = row.c1.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_GroupBy_ReturnsGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region
                    FROM orders
                    GROUP BY Region
                    """;
            },
            """
            Region
            North
            South
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Select(group => new TdsProjection() {Region = group.Key})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_GroupByMultiColumns_ReturnsGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, Amount, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region, Amount
                    ORDER BY Region, Amount
                    """;
            },
            """
            Region Amount Count
            North 10 1
            North 20 1
            South 5 1
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => new TdsCarrier() {Region = row.Region, Amount = row.Amount}).Select(group => new TdsProjection() {Region = group.Key.Region, Amount = group.Key.Amount, Count = group.Count()}).OrderBy(row => row.Region).ThenBy(row => row.Amount)");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_OrderByWithGroupBy_ReturnsOrderedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    ORDER BY Region DESC
                    """;
            },
            """
            Region Count
            South 1
            North 2
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()}).OrderByDescending(row => row.Region)");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Having_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) > 1
                    """;
            },
            """
            Region Count
            North 2
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => (group.Count() > 1)).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_HavingOr_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) = 1 OR COUNT(*) = 2
                    ORDER BY Region
                    """;
            },
            """
            Region Count
            North 2
            South 1
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => ((group.Count() == 1) OrElse (group.Count() == 2))).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()}).OrderBy(row => row.Region)");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_HavingEqualsOperator_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) = 2
                    """;
            },
            """
            Region Count
            North 2
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => (group.Count() == 2)).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_HavingNotEqualsOperator_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) <> 2
                    """;
            },
            """
            Region Count
            South 1
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => (group.Count() != 2)).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_HavingGreaterThanOperator_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) > 1
                    """;
            },
            """
            Region Count
            North 2
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => (group.Count() > 1)).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_HavingGreaterThanOrEqualOperator_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) >= 2
                    """;
            },
            """
            Region Count
            North 2
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => (group.Count() >= 2)).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_HavingLessThanOperator_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) < 2
                    """;
            },
            """
            Region Count
            South 1
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => (group.Count() < 2)).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_HavingLessThanOrEqualOperator_FiltersGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, COUNT(*) AS Count
                    FROM orders
                    GROUP BY Region
                    HAVING COUNT(*) <= 1
                    """;
            },
            """
            Region Count
            South 1
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Where(group => (group.Count() <= 1)).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Sum_ReturnsGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, SUM(Amount) AS TotalAmount
                    FROM orders
                    GROUP BY Region
                    """;
            },
            """
            Region TotalAmount
            North 30
            South 5
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Select(group => new TdsProjection() {Region = group.Key, TotalAmount = group.Sum(row => row.Amount)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Min_ReturnsGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, MIN(Amount) AS MinAmount
                    FROM orders
                    GROUP BY Region
                    """;
            },
            """
            Region MinAmount
            North 10
            South 5
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Select(group => new TdsProjection() {Region = group.Key, MinAmount = group.Min(row => row.Amount)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Max_ReturnsGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, MAX(Amount) AS MaxAmount
                    FROM orders
                    GROUP BY Region
                    """;
            },
            """
            Region MaxAmount
            North 20
            South 5
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Select(group => new TdsProjection() {Region = group.Key, MaxAmount = group.Max(row => row.Amount)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Avg_ReturnsGroupedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Region, AVG(Amount) AS AvgAmount
                    FROM orders
                    GROUP BY Region
                    """;
            },
            """
            Region AvgAmount
            North 15
            South 5
            """,
            expectedMaterializedQueries: "Order[].GroupBy(row => row.Region).Select(group => new TdsProjection() {Region = group.Key, AvgAmount = group.Average(row => row.Amount)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Parameters_UsesSpExecuteSqlParameters()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id > @id
                    """;
                _ = command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = 1 });
            },
            """
            Id
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id > 1)).Select(row => new TdsProjection() {Id = row.Id})");
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
            expectedMaterializedQueries: "");
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
        command.CommandText = """
            SELECT Id
            FROM customers
            WHERE Id =
            """;

        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteReaderAsync());
        Assert.Equal(50004, exception.Number);
        Assert.True(await invalidQueryTask.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static TdsQueryEngineOptions CreateQueryEngineOptions()
    {
        var options = new TdsQueryEngineOptions();
        options.AddQueryRoot("customers", GetCustomers());
        options.AddQueryRoot("orders", GetOrders());
        options.AddQueryRoot("nullable_customers", GetNullableCustomers());
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
            new Order(1, "North", 10),
            new Order(2, "North", 20),
            new Order(3, "South", 5),
        ];
    }

    private static NullableCustomer[] GetNullableCustomers()
    {
        return
        [
            new NullableCustomer(1, "Alice"),
            new NullableCustomer(2, "Bob"),
            new NullableCustomer(3, null),
        ];
    }

    private static async Task ExecuteQuery(TdsQueryEngineOptions queryEngineOptions, Action<SqlCommand> configureCommand, string expected, string expectedMaterializedQueries)
    {
        var materializedQueries = new List<string>();
        var materializeAsync = queryEngineOptions.MaterializeAsync;
        queryEngineOptions.MaterializeAsync = async (query, cancellationToken) =>
        {
            materializedQueries.Add(NormalizeMaterializedQuery(query.ToString() ?? string.Empty));
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
        Assert.Equal(NormalizeMultilineString(expectedMaterializedQueries), NormalizeMultilineString(string.Join('\n', materializedQueries)));
    }

    private static string NormalizeMaterializedQuery(string query)
    {
        query = query.Replace(typeof(TdsQueryEngineTests).FullName + "+", "", StringComparison.Ordinal);
        query = RemoveGeneratedTypeSuffix(query, "TdsProjection");

        return RemoveGeneratedTypeSuffix(query, "TdsCarrier");
    }

    private static string RemoveGeneratedTypeSuffix(string value, string typeName)
    {
        var index = value.IndexOf(typeName, StringComparison.Ordinal);
        if (index < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var startIndex = 0;
        while (index >= 0)
        {
            builder.Append(value, startIndex, index + typeName.Length - startIndex);

            var endIndex = index + typeName.Length;
            while (endIndex < value.Length && char.IsDigit(value[endIndex]))
            {
                endIndex++;
            }

            startIndex = endIndex;
            index = value.IndexOf(typeName, startIndex, StringComparison.Ordinal);
        }

        builder.Append(value, startIndex, value.Length - startIndex);

        return builder.ToString();
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

    private sealed record Order(int Id, string Region, int Amount);

    private sealed record NullableCustomer(int Id, string? Name);
}
