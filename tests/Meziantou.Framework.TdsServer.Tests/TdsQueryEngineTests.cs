using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
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
    public async Task SqlClient_QueryEngine_Cte_ReturnsRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    WITH filtered_customers AS
                    (
                        SELECT Id, Name
                        FROM customers
                        WHERE Id > 1
                    )
                    SELECT Id
                    FROM filtered_customers
                    ORDER BY Id
                    """;
            },
            """
            Id
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id > 1)).Select(row => new TdsProjection() {Id = row.Id, Name = row.Name}).OrderBy(row => row.Id).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CteColumnList_ReturnsRenamedColumns()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    WITH filtered_customers(CustomerId, CustomerName) AS
                    (
                        SELECT Id, Name
                        FROM customers
                        WHERE Id = 1
                    )
                    SELECT CustomerId, CustomerName
                    FROM filtered_customers
                    """;
            },
            """
            CustomerId CustomerName
            1 Alice
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id == 1)).Select(row => new TdsProjection() {Id = row.Id, Name = row.Name}).Select(row => new TdsProjection() {CustomerId = row.Id, CustomerName = row.Name}).Select(row => new TdsProjection() {CustomerId = row.CustomerId, CustomerName = row.CustomerName})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_NonAggregateFunctions_ReturnsComputedColumns()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT UPPER(Name) AS UpperName, LEN(Name) AS NameLength
                    FROM customers
                    WHERE Id = 1
                    """;
            },
            """
            UpperName NameLength
            ALICE 5
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id == 1)).Select(row => new TdsProjection() {UpperName = row.Name.ToUpperInvariant(), NameLength = row.Name.Length})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CustomFunctionMapping_UsesConfiguredMethod()
    {
        var queryEngineOptions = CreateQueryEngineOptions();
        queryEngineOptions.AddScalarFunction(
            "UPPER",
            arguments =>
            {
                if (arguments.Count != 1)
                {
                    throw new InvalidOperationException("UPPER requires exactly one argument.");
                }

                return Expression.Call(arguments[0], typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!);
            });

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT UPPER(Name) AS UpperName
                    FROM customers
                    WHERE Id = 1
                    """;
            },
            """
            UpperName
            ALICE
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id == 1)).Select(row => new TdsProjection() {UpperName = row.Name.ToUpper()})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_StringConcatenationAndArithmetic_ReturnsComputedColumns()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Name + Name AS NameConcat, Id + Id AS SumId
                    FROM customers
                    WHERE Id = 1
                    """;
            },
            """
            NameConcat SumId
            AliceAlice 2
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Id == 1)).Select(row => new TdsProjection() {NameConcat = Concat(row.Name, row.Name), SumId = (row.Id + row.Id)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_StringConcatenationPlusOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Name + '!' AS Value
                    FROM customers
                    WHERE Id = 1
                    """;
            },
            """
            Value
            Alice!
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 1)).Select(row => new TdsProjection() {Value = Concat(row.Name, "!")})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_StringConcatenationDoublePipeOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Name || '!' AS Value
                    FROM customers
                    WHERE Id = 1
                    """;
            },
            """
            Value
            Alice!
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 1)).Select(row => new TdsProjection() {Value = Concat(row.Name, "!")})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ArithmeticAdditionOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id + 1 AS Value
                    FROM customers
                    WHERE Id = 4
                    """;
            },
            """
            Value
            5
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 4)).Select(row => new TdsProjection() {Value = (row.Id + 1)})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ArithmeticSubtractionOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id - 1 AS Value
                    FROM customers
                    WHERE Id = 4
                    """;
            },
            """
            Value
            3
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 4)).Select(row => new TdsProjection() {Value = (row.Id - 1)})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ArithmeticSubtractionWithIntCastOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id - CAST(1 AS int) AS Value
                    FROM customers
                    WHERE Id = 4
                    """;
            },
            """
            Value
            3
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 4)).Select(row => new TdsProjection() {Value = (row.Id - Convert(ConvertToTypeCore(Convert(1, Object), System.Int32), Int32))})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ArithmeticMultiplicationOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id * 2 AS Value
                    FROM customers
                    WHERE Id = 4
                    """;
            },
            """
            Value
            8
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 4)).Select(row => new TdsProjection() {Value = (row.Id * 2)})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ArithmeticDivisionOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id / 2 AS Value
                    FROM customers
                    WHERE Id = 4
                    """;
            },
            """
            Value
            2
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 4)).Select(row => new TdsProjection() {Value = (row.Id / 2)})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ArithmeticModuloOperator_ReturnsComputedColumn()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id % 3 AS Value
                    FROM customers
                    WHERE Id = 4
                    """;
            },
            """
            Value
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (row.Id == 4)).Select(row => new TdsProjection() {Value = (row.Id % 3)})""");
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
    public async Task SqlClient_QueryEngine_WhereArithmeticExpression_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id + Id = 4
                    """;
            },
            """
            Id
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(row => ((row.Id + row.Id) == 4)).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereScalarFunctionExpression_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE LEN(Name) = 3
                    """;
            },
            """
            Id
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(row => (row.Name.Length == 3)).Select(row => new TdsProjection() {Id = row.Id})");
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
    public async Task SqlClient_QueryEngine_WhereExistsSubquery_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE EXISTS (SELECT 1
                    FROM orders
                    WHERE Id = 1)
                    ORDER BY Id
                    """;
            },
            """
            Id
            1
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => Order[].Where(row => (row.Id == 1)).Any()).OrderBy(row => row.Id).Select(row => new TdsProjection() {Id = row.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_WhereNotExistsSubquery_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE NOT EXISTS (SELECT 1
                    FROM orders
                    WHERE Id = 999)
                    ORDER BY Id
                    """;
            },
            """
            Id
            1
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(row => Not(Order[].Where(row => (row.Id == 999)).Any())).OrderBy(row => row.Id).Select(row => new TdsProjection() {Id = row.Id})");
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
    public async Task SqlClient_QueryEngine_LTrimFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE LTRIM('  Alice') = 'Alice' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (("  Alice".TrimStart() == "Alice") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_RTrimFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE RTRIM('Alice  ') = 'Alice' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (("Alice  ".TrimEnd() == "Alice") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_TrimFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE TRIM('  Alice  ') = 'Alice' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => (("  Alice  ".Trim() == "Alice") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_LeftFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE LEFT(Name, 2) = 'Al' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((SubstringCore(row.Name, 0, 2) == "Al") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_RightFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE RIGHT(Name, 2) = 'ce' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((SubstringCore(row.Name, (row.Name.Length - 2), 2) == "ce") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_SubstringFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE SUBSTRING(Name, 2, 3) = 'lic' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((SubstringCore(row.Name, (2 - 1), 3) == "lic") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ReplaceFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE REPLACE(Name, 'li', 'xx') = 'Axxce' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((row.Name.Replace("li", "xx") == "Axxce") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_TranslateFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE TRANSLATE(Name, 'Aie', 'a13') = 'al1c3' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((TranslateCore(row.Name, "Aie", "a13") == "al1c3") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_StuffFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE STUFF(Name, 2, 2, 'XX') = 'AXXce' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((StuffCore(row.Name, 2, 2, "XX") == "AXXce") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_StringEscapeFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE STRING_ESCAPE('a"b', 'json') = 'a\u0022b' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((StringEscapeCore("a"b", "json") == "a\u0022b") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_FormatFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE FORMAT(Id, 'D4') = '0001' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((FormatCore(Convert(row.Id, Object), "D4") == "0001") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_IsNullFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM nullable_customers
                    WHERE ISNULL(Name, 'fallback') = 'fallback'
                    """;
            },
            """
            Id
            3
            """,
            expectedMaterializedQueries: """NullableCustomer[].Where(row => ((row.Name ?? "fallback") == "fallback")).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CoalesceFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM nullable_customers
                    WHERE COALESCE(Name, 'fallback') = 'fallback'
                    """;
            },
            """
            Id
            3
            """,
            expectedMaterializedQueries: """NullableCustomer[].Where(row => ((row.Name ?? "fallback") == "fallback")).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CoalesceFunction_WithThreeArguments_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM nullable_customers
                    WHERE COALESCE(Name, 'fallback', 'other') = 'fallback'
                    """;
            },
            """
            Id
            3
            """,
            expectedMaterializedQueries: """NullableCustomer[].Where(row => (((row.Name ?? "fallback") ?? "other") == "fallback")).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_NullIfFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE NULLIF(Name, 'Alice') IS NULL AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((IIF((row.Name == "Alice"), null, row.Name) == null) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_IifFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE IIF(Id > 1, 'Y', 'N') = 'Y' AND Id = 2
                    """;
            },
            """
            Id
            2
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((IIF((row.Id > 1), "Y", "N") == "Y") AndAlso (row.Id == 2))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ChooseFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE CHOOSE(2, 'A', 'B', 'C') = 'B' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((IIF((2 == 1), "A", IIF((2 == 2), "B", IIF((2 == 3), "C", null))) == "B") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CastFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE CAST(Id AS bigint) = 1 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Convert(ConvertToTypeCore(Convert(row.Id, Object), System.Int64), Int64) == 1) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ConvertFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE CONVERT(varchar(10), Id, 2) = '1' AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Convert(ConvertToTypeCore(Convert(row.Id, Object), System.String), String) == "1") AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_TryCastFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE TRY_CAST('1' AS int) = 1 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Convert(TryConvertToTypeCore(Convert("1", Object), System.Int32), Nullable`1) == 1) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_TryConvertFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE TRY_CONVERT(int, '1', 0) = 1 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Convert(TryConvertToTypeCore(Convert("1", Object), System.Int32), Nullable`1) == 1) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ParseFunction_ReturnsServerError()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE PARSE('1' AS int) = 1 AND Id = 1
                    """;
            },
            expected: string.Empty,
            expectedMaterializedQueries: string.Empty));

        Assert.Equal(50004, exception.Number);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_TryParseFunction_ReturnsServerError()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE TRY_PARSE('1' AS int) = 1 AND Id = 1
                    """;
            },
            expected: string.Empty,
            expectedMaterializedQueries: string.Empty));

        Assert.Equal(50004, exception.Number);
    }

    [Fact]
    public async Task SqlClient_QueryEngine_GetDateFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE GETDATE() > CAST('1970-01-01' AS datetime) AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((DateTime.UtcNow > Convert(ConvertToTypeCore(Convert("1970-01-01", Object), System.DateTime), DateTime)) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_SysDateTimeFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE SYSDATETIME() > CAST('1970-01-01' AS datetime) AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((DateTime.UtcNow > Convert(ConvertToTypeCore(Convert("1970-01-01", Object), System.DateTime), DateTime)) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_DateAddFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE DATEADD(day, 1, CAST('2024-01-01' AS datetime)) > CAST('2024-01-01' AS datetime) AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((DateAddCore("day", 1, Convert(ConvertToTypeCore(Convert("2024-01-01", Object), System.DateTime), DateTime)) > Convert(ConvertToTypeCore(Convert("2024-01-01", Object), System.DateTime), DateTime)) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_DateDiffFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE DATEDIFF(day, CAST('2024-01-01' AS datetime), CAST('2024-01-03' AS datetime)) = 2 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((DateDiffCore("day", Convert(ConvertToTypeCore(Convert("2024-01-01", Object), System.DateTime), DateTime), Convert(ConvertToTypeCore(Convert("2024-01-03", Object), System.DateTime), DateTime)) == 2) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_EoMonthFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE DAY(EOMONTH(CAST('2024-02-10' AS datetime))) = 29 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((EoMonthCore(Convert(ConvertToTypeCore(Convert("2024-02-10", Object), System.DateTime), DateTime), 0).Day == 29) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_YearFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE YEAR(CAST('2024-02-10' AS datetime)) = 2024 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Convert(ConvertToTypeCore(Convert("2024-02-10", Object), System.DateTime), DateTime).Year == 2024) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_MonthFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE MONTH(CAST('2024-02-10' AS datetime)) = 2 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Convert(ConvertToTypeCore(Convert("2024-02-10", Object), System.DateTime), DateTime).Month == 2) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_DayFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE DAY(CAST('2024-02-10' AS datetime)) = 10 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Convert(ConvertToTypeCore(Convert("2024-02-10", Object), System.DateTime), DateTime).Day == 10) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_AbsFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE ABS(-3) = 3 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Abs(-3) == 3) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_RoundFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE ROUND(1.26, 1) = 1.3 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Round(Convert(ConvertToTypeCore(Convert(1.26, Object), System.Double), Double), 1) == 1.3) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CeilingFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE CEILING(1.2) = 2 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Ceiling(Convert(ConvertToTypeCore(Convert(1.2, Object), System.Double), Double)) == 2) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_FloorFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE FLOOR(1.8) = 1 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Floor(Convert(ConvertToTypeCore(Convert(1.8, Object), System.Double), Double)) == 1) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_PowerFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE POWER(2, 3) = 8 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Pow(Convert(ConvertToTypeCore(Convert(2, Object), System.Double), Double), Convert(ConvertToTypeCore(Convert(3, Object), System.Double), Double)) == 8) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_SqrtFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE SQRT(9) = 3 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Sqrt(Convert(ConvertToTypeCore(Convert(9, Object), System.Double), Double)) == 3) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ExpFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE EXP(1) > 2 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Exp(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 2) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_LogFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE LOG(8) > 2 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Log(Convert(ConvertToTypeCore(Convert(8, Object), System.Double), Double)) > 2) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_SinFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE SIN(0) = 0 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Sin(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 0) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CosFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE COS(0) = 1 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Cos(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 1) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_TanFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE TAN(0) = 0 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Tan(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 0) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_AsinFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE ASIN(0) = 0 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Asin(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 0) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_AcosFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE ACOS(1) = 0 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Acos(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) == 0) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_AtanFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE ATAN(1) > 0 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Atan(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 0) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_Atn2Function_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE ATN2(1, 1) > 0 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((Atan2(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double), Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 0) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CotFunction_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE COT(1) > 0 AND Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: """Customer[].Where(row => ((CotCore(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 0) AndAlso (row.Id == 1))).Select(row => new TdsProjection() {Id = row.Id})""");
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
        var actualMaterializedQueries = NormalizeMultilineString(string.Join('\n', materializedQueries));
        AssertMaterializedQueries(expectedMaterializedQueries, actualMaterializedQueries);
    }

    private static void AssertMaterializedQueries(string expectedMaterializedQueries, string actualMaterializedQueries)
    {
        var expected = NormalizeMultilineString(expectedMaterializedQueries);
        Assert.Equal(expected, actualMaterializedQueries);
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
