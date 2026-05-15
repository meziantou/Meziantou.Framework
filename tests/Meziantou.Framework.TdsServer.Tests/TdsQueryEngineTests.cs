using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Security.Claims;
using System.Text;
using Meziantou.Framework.Tds;
using Meziantou.Framework.Tds.Handler;
using Meziantou.Framework.Tds.QueryEngine;
using Microsoft.Data.SqlClient;
using Meziantou.Xunit;
using Xunit;

namespace Meziantou.Framework.Tds.Tests;

[RunIf(globalizationMode: TestGlobalizationMode.Disabled)]
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
            expectedMaterializedQueries: "Customer[].Select(customer => new TdsProjection() {Id = customer.Id, Name = customer.Name})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_QueryRootFactory_CanFilterUsingUserContext()
    {
        var queryEngineOptions = new TdsQueryEngineOptions();
        queryEngineOptions.AddQueryRoot(
            "customers",
            context =>
            {
                var userIdClaim = context.UserContext?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(userIdClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
                {
                    return Array.Empty<Customer>().AsQueryable();
                }

                return GetCustomers().Where(customer => customer.Id == userId).AsQueryable();
            });

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id, Name
                    FROM customers
                    """;
            },
            expected: """
                Id Name
                2 Bob
                """,
            expectedMaterializedQueries: null,
            userContext: CreateUserContext("2"));
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ParameterNames_AreTypeBasedAndUnique()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    WHERE Id = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 1)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ParameterNames_AreTypeBasedInSelect()
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
            expectedMaterializedQueries: "Customer[].Select(customer => new TdsProjection() {Id = customer.Id, Name = customer.Name})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ParameterNames_AreTypeBasedInJoinSelect()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
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
            expectedMaterializedQueries: "Customer[].Join(Customer[], customer => customer.Id, customer2 => customer2.Id, (customer3, customer4) => new TdsCarrier() {c1 = customer3, c2 = customer4}).Select(carrier => new TdsProjection() {Id = carrier.c1.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_LeftJoin_ReturnsRowsWithNullsForMissingMatches()
    {
        var queryEngineOptions = CreateQueryEngineOptions();
#if NET10_0_OR_GREATER
        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT c.Id, o.Region
                    FROM customers c
                    LEFT JOIN orders o ON c.Id = o.Id
                    ORDER BY c.Id
                    """;
            },
            """
            Id Region
            1 North
            2 North
            4 NULL
            """,
            expectedMaterializedQueries: "Customer[].LeftJoin(Order[], customer => customer.Id, order => order.Id, (customer2, order2) => new TdsCarrier() {c = customer2, o = order2}).OrderBy(carrier => carrier.c.Id).Select(carrier2 => new TdsProjection() {Id = carrier2.c.Id, Region = IIF((carrier2.o == null), null, carrier2.o.Region)})");
#else
        await ExecuteQueryExpectingServerError(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT c.Id, o.Region
                    FROM customers c
                    LEFT JOIN orders o ON c.Id = o.Id
                    ORDER BY c.Id
                    """;
            });
#endif
    }

    [Fact]
    public async Task SqlClient_QueryEngine_RightJoin_ReturnsRowsFromRightSource()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

#if NET10_0_OR_GREATER
        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT o.Id, o.Region
                    FROM customers c
                    RIGHT JOIN orders o ON c.Id = o.Id
                    ORDER BY o.Id
                    """;
            },
            """
            Id Region
            1 North
            2 North
            3 South
            """,
            expectedMaterializedQueries: "Customer[].RightJoin(Order[], customer => customer.Id, order => order.Id, (customer2, order2) => new TdsCarrier() {c = customer2, o = order2}).OrderBy(carrier => carrier.o.Id).Select(carrier2 => new TdsProjection() {Id = carrier2.o.Id, Region = carrier2.o.Region})");
#else
        await ExecuteQueryExpectingServerError(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT o.Id, o.Region
                    FROM customers c
                    RIGHT JOIN orders o ON c.Id = o.Id
                    ORDER BY o.Id
                    """;
            });
#endif
    }

    [Fact]
    public async Task SqlClient_QueryEngine_ParameterNames_AreTypeBasedInCorrelatedSubquery()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT c.Id
                    FROM customers c
                    WHERE EXISTS (SELECT 1
                    FROM orders o
                    WHERE o.Id = c.Id)
                    ORDER BY c.Id
                    """;
            },
            """
            Id
            1
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(customer => Order[].Where(order => (order.Id == customer.Id)).Any()).OrderBy(customer2 => customer2.Id).Select(customer3 => new TdsProjection() {Id = customer3.Id})");
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
            expectedMaterializedQueries: "Order[].OrderBy(order => order.Region).Select(order2 => new TdsProjection() {Region = order2.Region}).Distinct()");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id <= 2)).Select(customer2 => new TdsProjection() {Id = customer2.Id}).Union(Customer[].Where(customer3 => (customer3.Id >= 2)).Select(customer4 => new TdsProjection() {Id = customer4.Id})).OrderBy(projection => projection.Id)");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id > 1)).Select(customer2 => new TdsProjection() {Id = customer2.Id, Name = customer2.Name}).OrderBy(projection => projection.Id).Select(projection2 => new TdsProjection() {Id = projection2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 1)).Select(customer2 => new TdsProjection() {Id = customer2.Id, Name = customer2.Name}).Select(projection => new TdsProjection() {CustomerId = projection.Id, CustomerName = projection.Name}).Select(projection2 => new TdsProjection() {CustomerId = projection2.CustomerId, CustomerName = projection2.CustomerName})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 1)).Select(customer2 => new TdsProjection() {UpperName = customer2.Name.ToUpperInvariant(), NameLength = customer2.Name.Length})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 1)).Select(customer2 => new TdsProjection() {UpperName = customer2.Name.ToUpper()})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 1)).Select(customer2 => new TdsProjection() {NameConcat = Concat(customer2.Name, customer2.Name), SumId = (customer2.Id + customer2.Id)})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 1)).Select(customer2 => new TdsProjection() {Value = Concat(customer2.Name, \"!\")})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 1)).Select(customer2 => new TdsProjection() {Value = Concat(customer2.Name, \"!\")})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 4)).Select(customer2 => new TdsProjection() {Value = (customer2.Id + 1)})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 4)).Select(customer2 => new TdsProjection() {Value = (customer2.Id - 1)})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 4)).Select(customer2 => new TdsProjection() {Value = (customer2.Id - Convert(ConvertToTypeCore(Convert(1, Object), System.Int32), Int32))})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 4)).Select(customer2 => new TdsProjection() {Value = (customer2.Id * 2)})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 4)).Select(customer2 => new TdsProjection() {Value = (customer2.Id / 2)})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 4)).Select(customer2 => new TdsProjection() {Value = (customer2.Id % 3)})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id > 1)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id == 2)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id != 2)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id > 2)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id >= 2)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id < 2)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id <= 2)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((customer.Id + customer.Id) == 4)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Name.Length == 3)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((customer.Id > 1) AndAlso (customer.Id < 4))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((customer.Id == 1) OrElse (customer.Id == 4))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "NullableCustomer[].Where(nullableCustomer => (nullableCustomer.Name == null)).Select(nullableCustomer2 => new TdsProjection() {Id = nullableCustomer2.Id})");
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
            expectedMaterializedQueries: "NullableCustomer[].Where(nullableCustomer => Not((nullableCustomer.Name == null))).Select(nullableCustomer2 => new TdsProjection() {Id = nullableCustomer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => new [] {1, 4}.Contains(customer.Id)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => Order[].Select(order => order.Id).Contains(customer.Id)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => Not(Order[].Select(order => order.Id).Contains(customer.Id))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => Order[].Where(order => (order.Id == 1)).Any()).OrderBy(customer2 => customer2.Id).Select(customer3 => new TdsProjection() {Id = customer3.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => Not(Order[].Where(order => (order.Id == 999)).Any())).OrderBy(customer2 => customer2.Id).Select(customer3 => new TdsProjection() {Id = customer3.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CorrelatedExistsSubquery_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT c.Id
                    FROM customers c
                    WHERE EXISTS (
                        SELECT 1
                        FROM orders o
                        WHERE o.Id = c.Id)
                    ORDER BY c.Id
                    """;
            },
            """
            Id
            1
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(customer => Order[].Where(order => (order.Id == customer.Id)).Any()).OrderBy(customer2 => customer2.Id).Select(customer3 => new TdsProjection() {Id = customer3.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_CorrelatedInSubquery_ReturnsFilteredRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT c.Id
                    FROM customers c
                    WHERE c.Id IN (SELECT o.Id
                    FROM orders o
                    WHERE o.Id = c.Id)
                    ORDER BY c.Id
                    """;
            },
            """
            Id
            1
            2
            """,
            expectedMaterializedQueries: "Customer[].Where(customer => Order[].Where(order => (order.Id == customer.Id)).Select(order2 => order2.Id).Contains(customer.Id)).OrderBy(customer2 => customer2.Id).Select(customer3 => new TdsProjection() {Id = customer3.Id})");
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
            expectedMaterializedQueries: "Customer[].OrderBy(customer => customer.Id).Select(customer2 => new TdsProjection() {Id = customer2.Id}).Take(2)");
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
            expectedMaterializedQueries: "Customer[].OrderBy(customer => customer.Id).Skip(1).Take(2).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].OrderByDescending(customer => customer.Name).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Join(Customer[], customer => customer.Id, customer2 => customer2.Id, (customer3, customer4) => new TdsCarrier() {c1 = customer3, c2 = customer4}).Select(carrier => new TdsProjection() {Id = carrier.c1.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_DerivedTableInFrom_ReturnsProjectedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT d.Id
                    FROM (SELECT Id
                    FROM customers
                    WHERE Id > 1) d
                    ORDER BY d.Id
                    """;
            },
            """
            Id
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id > 1)).Select(customer2 => new TdsProjection() {Id = customer2.Id}).OrderBy(projection => projection.Id).Select(projection2 => new TdsProjection() {Id = projection2.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_DerivedTableInJoin_ReturnsProjectedRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT c.Id
                    FROM customers c
                    INNER JOIN (SELECT Id
                    FROM customers
                    WHERE Id > 1) d ON c.Id = d.Id
                    ORDER BY c.Id
                    """;
            },
            """
            Id
            2
            4
            """,
            expectedMaterializedQueries: "Customer[].Join(Customer[].Where(customer => (customer.Id > 1)).Select(customer2 => new TdsProjection() {Id = customer2.Id}), customer3 => customer3.Id, projection => projection.Id, (customer4, projection2) => new TdsCarrier() {c = customer4, d = projection2}).OrderBy(carrier => carrier.c.Id).Select(carrier2 => new TdsProjection() {Id = carrier2.c.Id})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Select(group => new TdsProjection() {Region = group.Key})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => new TdsCarrier() {Region = order.Region, Amount = order.Amount}).Select(group => new TdsProjection() {Region = group.Key.Region, Amount = group.Key.Amount, Count = group.Count()}).OrderBy(projection => projection.Region).ThenBy(projection2 => projection2.Amount)");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Select(group => new TdsProjection() {Region = group.Key, Count = group.Count()}).OrderByDescending(projection => projection.Region)");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => (group.Count() > 1)).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => ((group.Count() == 1) OrElse (group.Count() == 2))).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()}).OrderBy(projection => projection.Region)");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => (group.Count() == 2)).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => (group.Count() != 2)).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => (group.Count() > 1)).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => (group.Count() >= 2)).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => (group.Count() < 2)).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Where(group => (group.Count() <= 1)).Select(group2 => new TdsProjection() {Region = group2.Key, Count = group2.Count()})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Select(group => new TdsProjection() {Region = group.Key, TotalAmount = group.Sum(order2 => order2.Amount)})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Select(group => new TdsProjection() {Region = group.Key, MinAmount = group.Min(order2 => order2.Amount)})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Select(group => new TdsProjection() {Region = group.Key, MaxAmount = group.Max(order2 => order2.Amount)})");
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
            expectedMaterializedQueries: "Order[].GroupBy(order => order.Region).Select(group => new TdsProjection() {Region = group.Key, AvgAmount = group.Average(order2 => order2.Amount)})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((\"  Alice\".TrimStart() == \"Alice\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((\"Alice  \".TrimEnd() == \"Alice\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((\"  Alice  \".Trim() == \"Alice\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((SubstringCore(customer.Name, 0, 2) == \"Al\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((SubstringCore(customer.Name, (customer.Name.Length - 2), 2) == \"ce\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((SubstringCore(customer.Name, (2 - 1), 3) == \"lic\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((customer.Name.Replace(\"li\", \"xx\") == \"Axxce\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((TranslateCore(customer.Name, \"Aie\", \"a13\") == \"al1c3\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((StuffCore(customer.Name, 2, 2, \"XX\") == \"AXXce\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((StringEscapeCore(\"a\"b\", \"json\") == \"a\\u0022b\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((FormatCore(Convert(customer.Id, Object), \"D4\") == \"0001\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "NullableCustomer[].Where(nullableCustomer => ((nullableCustomer.Name ?? \"fallback\") == \"fallback\")).Select(nullableCustomer2 => new TdsProjection() {Id = nullableCustomer2.Id})");
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
            expectedMaterializedQueries: "NullableCustomer[].Where(nullableCustomer => ((nullableCustomer.Name ?? \"fallback\") == \"fallback\")).Select(nullableCustomer2 => new TdsProjection() {Id = nullableCustomer2.Id})");
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
            expectedMaterializedQueries: "NullableCustomer[].Where(nullableCustomer => (((nullableCustomer.Name ?? \"fallback\") ?? \"other\") == \"fallback\")).Select(nullableCustomer2 => new TdsProjection() {Id = nullableCustomer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((IIF((customer.Name == \"Alice\"), null, customer.Name) == null) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((IIF((customer.Id > 1), \"Y\", \"N\") == \"Y\") AndAlso (customer.Id == 2))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((IIF((2 == 1), \"A\", IIF((2 == 2), \"B\", IIF((2 == 3), \"C\", null))) == \"B\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Convert(ConvertToTypeCore(Convert(customer.Id, Object), System.Int64), Int64) == 1) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Convert(ConvertToTypeCore(Convert(customer.Id, Object), System.String), String) == \"1\") AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Convert(TryConvertToTypeCore(Convert(\"1\", Object), System.Int32), Nullable`1) == 1) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Convert(TryConvertToTypeCore(Convert(\"1\", Object), System.Int32), Nullable`1) == 1) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((DateTime.UtcNow > Convert(ConvertToTypeCore(Convert(\"1970-01-01\", Object), System.DateTime), DateTime)) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((DateTime.UtcNow > Convert(ConvertToTypeCore(Convert(\"1970-01-01\", Object), System.DateTime), DateTime)) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((DateAddCore(\"day\", 1, Convert(ConvertToTypeCore(Convert(\"2024-01-01\", Object), System.DateTime), DateTime)) > Convert(ConvertToTypeCore(Convert(\"2024-01-01\", Object), System.DateTime), DateTime)) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((DateDiffCore(\"day\", Convert(ConvertToTypeCore(Convert(\"2024-01-01\", Object), System.DateTime), DateTime), Convert(ConvertToTypeCore(Convert(\"2024-01-03\", Object), System.DateTime), DateTime)) == 2) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((EoMonthCore(Convert(ConvertToTypeCore(Convert(\"2024-02-10\", Object), System.DateTime), DateTime), 0).Day == 29) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Convert(ConvertToTypeCore(Convert(\"2024-02-10\", Object), System.DateTime), DateTime).Year == 2024) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Convert(ConvertToTypeCore(Convert(\"2024-02-10\", Object), System.DateTime), DateTime).Month == 2) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Convert(ConvertToTypeCore(Convert(\"2024-02-10\", Object), System.DateTime), DateTime).Day == 10) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Abs(-3) == 3) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Round(Convert(ConvertToTypeCore(Convert(1.26, Object), System.Double), Double), 1) == 1.3) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Ceiling(Convert(ConvertToTypeCore(Convert(1.2, Object), System.Double), Double)) == 2) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Floor(Convert(ConvertToTypeCore(Convert(1.8, Object), System.Double), Double)) == 1) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Pow(Convert(ConvertToTypeCore(Convert(2, Object), System.Double), Double), Convert(ConvertToTypeCore(Convert(3, Object), System.Double), Double)) == 8) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Sqrt(Convert(ConvertToTypeCore(Convert(9, Object), System.Double), Double)) == 3) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Exp(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 2) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Log(Convert(ConvertToTypeCore(Convert(8, Object), System.Double), Double)) > 2) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Sin(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 0) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Cos(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 1) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Tan(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 0) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Asin(Convert(ConvertToTypeCore(Convert(0, Object), System.Double), Double)) == 0) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Acos(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) == 0) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Atan(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 0) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((Atan2(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double), Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 0) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
            expectedMaterializedQueries: "Customer[].Where(customer => ((CotCore(Convert(ConvertToTypeCore(Convert(1, Object), System.Double), Double)) > 0) AndAlso (customer.Id == 1))).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_IsJsonFunction_ReturnsExpectedValues()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT ISJSON(Payload) AS IsJson
                    FROM json_docs
                    WHERE Id = 1
                    """;
            },
            """
            IsJson
            1
            """,
            expectedMaterializedQueries: "JsonDocumentRow[].Where(jsonDocumentRow => (jsonDocumentRow.Id == 1)).Select(jsonDocumentRow2 => new TdsProjection() {IsJson = IsJsonCore(jsonDocumentRow2.Payload, null)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_JsonValueFunction_LaxMode_ReturnsNullForMissingPath()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT JSON_VALUE(Payload, 'lax $.address.country') AS Country
                    FROM json_docs
                    WHERE Id = 1
                    """;
            },
            """
            Country
            NULL
            """,
            expectedMaterializedQueries: "JsonDocumentRow[].Where(jsonDocumentRow => (jsonDocumentRow.Id == 1)).Select(jsonDocumentRow2 => new TdsProjection() {Country = JsonValueCore(jsonDocumentRow2.Payload, \"lax $.address.country\")})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_JsonValueFunction_StrictMode_ThrowsForMissingPath()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQueryExpectingServerError(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT JSON_VALUE(Payload, 'strict $.address.country') AS Country
                    FROM json_docs
                    WHERE Id = 1
                    """;
            });
    }

    [Fact]
    public async Task SqlClient_QueryEngine_JsonPathExistsFunction_SupportsLaxAndStrictModes()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT JSON_PATH_EXISTS(Payload, 'lax $.address.country') AS LaxExists, JSON_PATH_EXISTS(Payload, 'strict $.address.city') AS StrictExists
                    FROM json_docs
                    WHERE Id = 1
                    """;
            },
            """
            LaxExists StrictExists
            0 1
            """,
            expectedMaterializedQueries: "JsonDocumentRow[].Where(jsonDocumentRow => (jsonDocumentRow.Id == 1)).Select(jsonDocumentRow2 => new TdsProjection() {LaxExists = JsonPathExistsCore(jsonDocumentRow2.Payload, \"lax $.address.country\"), StrictExists = JsonPathExistsCore(jsonDocumentRow2.Payload, \"strict $.address.city\")})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_JsonQueryFunction_LaxMode_ReturnsNullForScalar()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT JSON_QUERY(Payload, 'lax $.name') AS NameFragment
                    FROM json_docs
                    WHERE Id = 1
                    """;
            },
            """
            NameFragment
            NULL
            """,
            expectedMaterializedQueries: "JsonDocumentRow[].Where(jsonDocumentRow => (jsonDocumentRow.Id == 1)).Select(jsonDocumentRow2 => new TdsProjection() {NameFragment = JsonQueryCore(jsonDocumentRow2.Payload, \"lax $.name\")})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_JsonQueryFunction_StrictMode_ThrowsForScalar()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQueryExpectingServerError(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT JSON_QUERY(Payload, 'strict $.name') AS NameFragment
                    FROM json_docs
                    WHERE Id = 1
                    """;
            });
    }

    [Fact]
    public async Task SqlClient_QueryEngine_XmlDataType_UntypedCast_ReturnsXml()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT CAST(Payload AS XML) AS PayloadXml
                    FROM xml_docs
                    WHERE Id = 1
                    """;
            },
            """
            PayloadXml
            <root><item id="1">Alpha</item><item id="2">Beta</item></root>
            """,
            expectedMaterializedQueries: "XmlDocumentRow[].Where(xmlDocumentRow => (xmlDocumentRow.Id == 1)).Select(xmlDocumentRow2 => new TdsProjection() {PayloadXml = ConvertToXmlCore(Convert(xmlDocumentRow2.Payload, Object), null, null, None)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_XmlMethod_Query_ReturnsXmlFragment()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Payload.query('/root/item[@id="1"]') AS Fragment
                    FROM xml_docs
                    WHERE Id = 1
                    """;
            },
            """
            Fragment
            <item id="1">Alpha</item>
            """,
            expectedMaterializedQueries: "XmlDocumentRow[].Where(xmlDocumentRow => (xmlDocumentRow.Id == 1)).Select(xmlDocumentRow2 => new TdsProjection() {Fragment = XmlQueryCore(Convert(xmlDocumentRow2.Payload, Object), \"/root/item[@id=\"1\"]\")})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_XmlMethod_Value_ReturnsScalarValue()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Payload.value('(/root/item[@id="2"]/text())[1]', 'nvarchar(max)') AS ItemValue
                    FROM xml_docs
                    WHERE Id = 1
                    """;
            },
            """
            ItemValue
            Beta
            """,
            expectedMaterializedQueries: "XmlDocumentRow[].Where(xmlDocumentRow => (xmlDocumentRow.Id == 1)).Select(xmlDocumentRow2 => new TdsProjection() {ItemValue = Convert(XmlValueCore(Convert(xmlDocumentRow2.Payload, Object), \"(/root/item[@id=\"2\"]/text())[1]\", System.String), String)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_XmlMethod_Exist_FiltersRows()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM xml_docs
                    WHERE Payload.exist('/root/item[@id=2]') = 1
                    """;
            },
            """
            Id
            1
            """,
            expectedMaterializedQueries: "XmlDocumentRow[].Where(xmlDocumentRow => (XmlExistCore(Convert(xmlDocumentRow.Payload, Object), \"/root/item[@id=2]\") == 1)).Select(xmlDocumentRow2 => new TdsProjection() {Id = xmlDocumentRow2.Id})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_XmlMethod_Nodes_ReturnsRowsFromCrossApply()
    {
        var queryEngineOptions = CreateQueryEngineOptions();

        await ExecuteQuery(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT n.Item.value('(@id)[1]', 'int') AS ItemId
                    FROM xml_docs
                    CROSS APPLY Payload.nodes('/root/item') AS n(Item)
                    WHERE Id = 1
                    ORDER BY n.Item.value('(@id)[1]', 'int')
                    """;
            },
            """
            ItemId
            1
            2
            """,
            expectedMaterializedQueries: "XmlDocumentRow[].SelectMany(xmlDocumentRow => XmlNodesCore(Convert(xmlDocumentRow.Payload, Object), \"/root/item\").Select(sqlXmlValue => new TdsProjection() {Item = sqlXmlValue}), (xmlDocumentRow2, projection) => new TdsCarrier() {xml_docs = xmlDocumentRow2, n = projection}).Where(carrier => (carrier.xml_docs.Id == 1)).OrderBy(carrier2 => Convert(XmlValueCore(Convert(carrier2.n.Item, Object), \"(@id)[1]\", System.Int32), Int32)).Select(carrier3 => new TdsProjection() {ItemId = Convert(XmlValueCore(Convert(carrier3.n.Item, Object), \"(@id)[1]\", System.Int32), Int32)})");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_XmlSchemaCollection_FromOptions_ValidatesTypedXmlCast()
    {
        var queryEngineOptions = CreateQueryEngineOptions();
        queryEngineOptions.AddXmlSchemaCollection("dbo.BasicXml", """
            <xsd:schema xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <xsd:element name="root">
                <xsd:complexType>
                  <xsd:sequence>
                    <xsd:element name="item" minOccurs="0" maxOccurs="unbounded">
                      <xsd:complexType>
                        <xsd:simpleContent>
                          <xsd:extension base="xsd:string">
                            <xsd:attribute name="id" type="xsd:int" use="required" />
                          </xsd:extension>
                        </xsd:simpleContent>
                      </xsd:complexType>
                    </xsd:element>
                  </xsd:sequence>
                </xsd:complexType>
              </xsd:element>
            </xsd:schema>
            """);
        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master")),
            TdsQueryEngine.CreateQueryHandler(queryEngineOptions));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        var payload = await ExecuteWithTransientSqlRetry(async () =>
        {
            await using var connection = new SqlConnection(CreateConnectionString(port));
            await connection.OpenAsync();

            await using var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = """
                SELECT CAST(Payload AS XML(dbo.BasicXml)) AS PayloadXml
                FROM xml_docs
                WHERE Id = 1
                """;
            await using var reader = await selectCommand.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            var value = reader.GetString(0);
            Assert.False(await reader.ReadAsync());
            return value;
        });

        Assert.Equal("<root><item id=\"1\">Alpha</item><item id=\"2\">Beta</item></root>", payload);
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
            expectedMaterializedQueries: "Customer[].Where(customer => (customer.Id > 1)).Select(customer2 => new TdsProjection() {Id = customer2.Id})");
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
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The stored procedure name is generated within the test and not user-controlled.")]
    public async Task SqlClient_QueryEngine_StoredProcedure_Unauthorized_ReturnsPermissionDenied()
    {
        var procedureName = "query_engine_proc_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var queryEngineOptions = CreateQueryEngineOptions();
        queryEngineOptions.StoredProcedures.Add(procedureName, () => GetCustomers());
        queryEngineOptions.IsAuthorized = (context, resourceKind, resourceName) =>
            resourceKind != TdsQueryEngineResourceKind.StoredProcedure ||
            !string.Equals(resourceName, procedureName, StringComparison.OrdinalIgnoreCase);

        await ExecuteQueryExpectingServerError(
            queryEngineOptions,
            command =>
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = procedureName;
            },
            expectedErrorNumber: 229,
            expectedMessageContains: "EXECUTE permission was denied");
    }

    [Fact]
    public async Task SqlClient_QueryEngine_QueryRoot_Unauthorized_ReturnsPermissionDenied()
    {
        var queryEngineOptions = CreateQueryEngineOptions();
        queryEngineOptions.IsAuthorized = (context, resourceKind, resourceName) =>
            resourceKind != TdsQueryEngineResourceKind.QueryRoot ||
            !string.Equals(resourceName, "customers", StringComparison.OrdinalIgnoreCase);

        await ExecuteQueryExpectingServerError(
            queryEngineOptions,
            command =>
            {
                command.CommandText = """
                    SELECT Id
                    FROM customers
                    """;
            },
            expectedErrorNumber: 229,
            expectedMessageContains: "SELECT permission was denied");
    }

    [Fact]
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The stored procedure name is generated within the test and not user-controlled.")]
    public async Task SqlClient_QueryEngine_UnknownStoredProcedure_RemainsNotFoundError()
    {
        var queryEngineOptions = CreateQueryEngineOptions();
        queryEngineOptions.IsAuthorized = (context, resourceKind, resourceName) => false;

        await ExecuteQueryExpectingServerError(
            queryEngineOptions,
            command =>
            {
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = "query_engine_missing_proc_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            },
            expectedErrorNumber: 50004,
            expectedMessageContains: "Unknown stored procedure");
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

        var exception = await ExecuteWithTransientSqlRetry(async () =>
        {
            await using var connection = new SqlConnection(CreateConnectionString(port));
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id
                FROM customers
                WHERE Id =
                """;

            return await Assert.ThrowsAsync<SqlException>(() => command.ExecuteReaderAsync());
        });

        Assert.Equal(50004, exception.Number);
        Assert.True(await invalidQueryTask.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static TdsQueryEngineOptions CreateQueryEngineOptions()
    {
        var options = new TdsQueryEngineOptions();
        options.AddQueryRoot("customers", context => GetCustomers().AsQueryable());
        options.AddQueryRoot("orders", context => GetOrders().AsQueryable());
        options.AddQueryRoot("nullable_customers", context => GetNullableCustomers().AsQueryable());
        options.AddQueryRoot("json_docs", context => GetJsonDocuments().AsQueryable());
        options.AddQueryRoot("xml_docs", context => GetXmlDocuments().AsQueryable());
        return options;
    }

    private static ClaimsPrincipal CreateUserContext(string userId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
        ],
        authenticationType: "password");
        return new ClaimsPrincipal(identity);
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

    private static XmlDocumentRow[] GetXmlDocuments()
    {
        return
        [
            new XmlDocumentRow(1, "<root><item id=\"1\">Alpha</item><item id=\"2\">Beta</item></root>"),
            new XmlDocumentRow(2, "<root><item id=\"3\">Gamma</item></root>"),
        ];
    }

    private static JsonDocumentRow[] GetJsonDocuments()
    {
        return
        [
            new JsonDocumentRow(1, """{"name":"Alice","address":{"city":"Paris"},"items":[1,2]}"""),
            new JsonDocumentRow(2, """{"name":"Bob"}"""),
            new JsonDocumentRow(3, "{ invalid }"),
            new JsonDocumentRow(4, null),
        ];
    }

    private static async Task ExecuteQuery(TdsQueryEngineOptions queryEngineOptions, Action<SqlCommand> configureCommand, string expected, string? expectedMaterializedQueries, ClaimsPrincipal? userContext = null)
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
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master", userContext)),
            TdsQueryEngine.CreateQueryHandler(queryEngineOptions));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        var actualResult = await ExecuteWithTransientSqlRetry(async () =>
        {
            await using var connection = new SqlConnection(CreateConnectionString(port));
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            configureCommand(command);

            await using var reader = await command.ExecuteReaderAsync();
            return await ReadResultAsync(reader);
        });

        Assert.Equal(NormalizeMultilineString(expected), actualResult);
        if (expectedMaterializedQueries is not null)
        {
            var actualMaterializedQueries = NormalizeMultilineString(string.Join('\n', materializedQueries));
            AssertMaterializedQueries(expectedMaterializedQueries, actualMaterializedQueries);
        }
    }

    private static async Task ExecuteQueryExpectingServerError(TdsQueryEngineOptions queryEngineOptions, Action<SqlCommand> configureCommand, int expectedErrorNumber = 50004, string? expectedMessageContains = null, ClaimsPrincipal? userContext = null)
    {
        var options = new TdsServerOptions();
        options.AddTcpListener(0, IPAddress.Loopback);

        using var server = new TdsServer(
            options,
            (context, cancellationToken) => ValueTask.FromResult(TdsAuthenticationResult.Success("master", userContext)),
            TdsQueryEngine.CreateQueryHandler(queryEngineOptions));

        await server.StartAsync();
        var port = Assert.Single(server.Ports);

        var exception = await ExecuteWithTransientSqlRetry(async () =>
        {
            await using var connection = new SqlConnection(CreateConnectionString(port));
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            configureCommand(command);

            return await Assert.ThrowsAsync<SqlException>(() => command.ExecuteReaderAsync());
        });

        Assert.Equal(expectedErrorNumber, exception.Number);
        if (!string.IsNullOrEmpty(expectedMessageContains))
        {
            Assert.Contains(expectedMessageContains, exception.Message, StringComparison.Ordinal);
        }
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
        return hasTimeout && hasPreLogin;
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

    private sealed record JsonDocumentRow(int Id, string? Payload);

    private sealed record XmlDocumentRow(int Id, string Payload);
}
