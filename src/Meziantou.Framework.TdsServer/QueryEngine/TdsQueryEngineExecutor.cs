using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.XPath;
using Meziantou.Framework.Tds.Handler;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using SqlParser = Microsoft.SqlServer.Management.SqlParser.Parser.Parser;
using SqlParserParseOptions = Microsoft.SqlServer.Management.SqlParser.Parser.ParseOptions;

namespace Meziantou.Framework.Tds.QueryEngine;

[SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Query translation helpers intentionally use abstract return types for flexibility.")]
internal sealed class TdsQueryEngineExecutor
{
    private readonly TdsQueryEngineOptions _options;

    public TdsQueryEngineExecutor(TdsQueryEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    public async ValueTask<TdsQueryResult> ExecuteAsync(TdsQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            if (context.RequestType == TdsQueryRequestType.Rpc && !string.Equals(context.ProcedureName, "sp_executesql", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteStoredProcedureAsync(context, cancellationToken).ConfigureAwait(false);
            }

            var commandText = GetCommandText(context);
            var parameters = GetSqlParameters(context);
            return await ExecuteTextQueryAsync(commandText, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (TdsQueryEngineException ex)
        {
            return TdsQueryResult.FromError(new TdsQueryError
            {
                Number = 50004,
                State = 1,
                Class = 16,
                Message = ex.Message,
            });
        }
    }

    private async ValueTask<TdsQueryResult> ExecuteStoredProcedureAsync(TdsQueryContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ProcedureName))
        {
            throw new TdsQueryEngineException("The RPC request does not specify a stored procedure name.");
        }

        if (!_options.StoredProcedures.TryGetValue(context.ProcedureName, out var storedProcedure))
        {
            throw new TdsQueryEngineException($"Unknown stored procedure '{context.ProcedureName}'.");
        }

        var value = await InvokeStoredProcedureAsync(storedProcedure, context.Parameters, cancellationToken).ConfigureAwait(false);
        return TdsQueryResultBuilder.FromValue(value);
    }

    private static async ValueTask<object?> InvokeStoredProcedureAsync(Delegate storedProcedure, IReadOnlyList<TdsQueryParameter> parameters, CancellationToken cancellationToken)
    {
        var methodParameters = storedProcedure.Method.GetParameters();
        var arguments = new object?[methodParameters.Length];
        for (var i = 0; i < methodParameters.Length; i++)
        {
            var parameter = methodParameters[i];
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                arguments[i] = cancellationToken;
                continue;
            }

            var queryParameter = parameters.FirstOrDefault(candidate => string.Equals(NormalizeName(candidate.Name), NormalizeName(parameter.Name), StringComparison.OrdinalIgnoreCase));
            if (queryParameter is null)
            {
                if (parameter.HasDefaultValue)
                {
                    arguments[i] = parameter.DefaultValue;
                    continue;
                }

                throw new TdsQueryEngineException($"Missing value for stored procedure parameter '{parameter.Name}'.");
            }

            arguments[i] = ConvertValue(queryParameter.Value, parameter.ParameterType);
        }

        var result = storedProcedure.DynamicInvoke(arguments);
        return await UnwrapAsyncResult(result).ConfigureAwait(false);
    }

    private static async ValueTask<object?> UnwrapAsyncResult(object? result)
    {
        if (result is null)
        {
            return null;
        }

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().IsGenericType ? task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task) : null;
        }

        if (result is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return null;
        }

        var resultType = result.GetType();
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var valueTaskAsTask = (Task)resultType.GetMethod(nameof(ValueTask<object>.AsTask))!.Invoke(result, [])!;
            await valueTaskAsTask.ConfigureAwait(false);
            return valueTaskAsTask.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(valueTaskAsTask);
        }

        return result;
    }

    private async ValueTask<TdsQueryResult> ExecuteTextQueryAsync(string commandText, IReadOnlyDictionary<string, TdsQueryParameter> parameters, CancellationToken cancellationToken)
    {
        var translatedQuery = TranslateQuery(commandText, parameters);
        var rows = await _options.MaterializeAsync(translatedQuery, cancellationToken).ConfigureAwait(false);
        return TdsQueryResultBuilder.FromRows(rows, translatedQuery.ElementType);
    }

    private IQueryable TranslateQuery(string commandText, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var parseResult = SqlParser.Parse(commandText, new SqlParserParseOptions(), out _);
        if (parseResult.Errors.Any() || parseResult.ParseErrors.Any())
        {
            throw new TdsQueryEngineException("Invalid SQL query.");
        }

        if (parseResult.Script.Batches.Count != 1 ||
            parseResult.Script.Batches[0].Statements.Count != 1 ||
            parseResult.Script.Batches[0].Statements[0] is not SqlSelectStatement selectStatement)
        {
            throw new TdsQueryEngineException("Only a single SELECT statement is supported.");
        }

        if (selectStatement.SelectSpecification.ForClause is not null)
        {
            throw new TdsQueryEngineException("The SQL query uses a SELECT feature that is not supported.");
        }

        var cteRoots = BuildCteRoots(selectStatement.QueryWithClause, parameters);
        IQueryable query;
        if (selectStatement.SelectSpecification.QueryExpression is SqlQuerySpecification querySpecification)
        {
            query = TranslateQuerySpecification(querySpecification, selectStatement.SelectSpecification.OrderByClause, parameters, cteRoots);
        }
        else
        {
            query = TranslateQueryExpression(selectStatement.SelectSpecification.QueryExpression, parameters, cteRoots);
            if (selectStatement.SelectSpecification.OrderByClause is not null)
            {
                query = ApplyOrderBy(CreateProjectionSource(query), selectStatement.SelectSpecification.OrderByClause, parameters).Query;
            }
        }

        return RenameQueryParameters(query);
    }

    private static IQueryable RenameQueryParameters(IQueryable query)
    {
        var visitor = new ParameterNameVisitor();
        var expression = visitor.Visit(query.Expression);
        return expression is null ? query : query.Provider.CreateQuery(expression);
    }

    private IReadOnlyDictionary<string, TdsQueryRoot> BuildCteRoots(SqlQueryWithClause? withClause, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var result = new Dictionary<string, TdsQueryRoot>(StringComparer.OrdinalIgnoreCase);
        if (withClause is null)
        {
            return result;
        }

        foreach (var cte in withClause.CommonTableExpressions)
        {
            var cteName = cte.Name.Value;
            var query = TranslateQueryExpression(cte.QueryExpression, parameters, result);
            if (cte.ColumnList is not null && cte.ColumnList.Count > 0)
            {
                query = ApplyColumnList(query, cte.ColumnList, scopeName: "CTE");
            }

            result.Add(cteName, new TdsQueryRoot(cteName, query));
        }

        return result;
    }

    private static IQueryable ApplyColumnList(IQueryable query, SqlIdentifierCollection columnList, string scopeName)
    {
        var members = GetReadableMembers(query.ElementType);
        if (members.Count != columnList.Count)
        {
            throw new TdsQueryEngineException($"{scopeName} column list count must match the number of projected columns.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectionMembers = new List<TdsProjectionMember>(columnList.Count);
        foreach (var (column, member) in columnList.Zip(members))
        {
            if (!names.Add(column.Value))
            {
                throw new TdsQueryEngineException($"Duplicate {scopeName} column name '{column.Value}'.");
            }

            projectionMembers.Add(new TdsProjectionMember(column.Value, GetMemberType(member)));
        }

        var projectionType = TdsProjectionTypeFactory.GetProjectionType(projectionMembers);
        var parameter = Expression.Parameter(query.ElementType, "row");
        var bindings = new List<MemberBinding>(columnList.Count);
        for (var i = 0; i < columnList.Count; i++)
        {
            var memberAccess = BuildMemberAccess(parameter, members[i]);
            bindings.Add(Expression.Bind(projectionType.GetProperty(columnList[i].Value)!, memberAccess));
        }

        var body = Expression.MemberInit(Expression.New(projectionType), bindings);
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [query.ElementType, projectionType],
            query.Expression,
            Expression.Quote(Expression.Lambda(body, parameter)));

        return query.Provider.CreateQuery(call);
    }

    private IQueryable TranslateQueryExpression(SqlQueryExpression queryExpression, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        return queryExpression switch
        {
            SqlQuerySpecification querySpecification => TranslateQuerySpecification(querySpecification, orderByOverride: null, parameters, cteRoots),
            SqlBinaryQueryExpression binaryQueryExpression => TranslateBinaryQueryExpression(binaryQueryExpression, parameters, cteRoots),
            _ => throw new TdsQueryEngineException("Only query specifications and UNION query expressions are supported."),
        };
    }

    private IQueryable TranslateBinaryQueryExpression(SqlBinaryQueryExpression queryExpression, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        var left = TranslateQueryExpression(queryExpression.Left, parameters, cteRoots);
        var right = TranslateQueryExpression(queryExpression.Right, parameters, cteRoots);
        if (left.ElementType != right.ElementType)
        {
            throw new TdsQueryEngineException("UNION query expressions must project the same columns and types.");
        }

        var methodName = queryExpression.Operator switch
        {
            SqlBinaryQueryOperatorType.Union => nameof(Queryable.Union),
            SqlBinaryQueryOperatorType.UnionAll => nameof(Queryable.Concat),
            _ => throw new TdsQueryEngineException($"Set operator '{queryExpression.Operator}' is not supported."),
        };
        var call = Expression.Call(
            typeof(Queryable),
            methodName,
            [left.ElementType],
            left.Expression,
            right.Expression);

        return left.Provider.CreateQuery(call);
    }

    private IQueryable TranslateQuerySpecification(SqlQuerySpecification querySpecification, SqlOrderByClause? orderByOverride, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        if (querySpecification.IntoClause is not null ||
            querySpecification.WindowClause is not null ||
            querySpecification.ForClause is not null)
        {
            throw new TdsQueryEngineException("The SQL query uses a SELECT feature that is not supported.");
        }

        var source = BuildSource(querySpecification.FromClause, cteRoots, parameters);
        if (querySpecification.WhereClause?.Expression is not null)
        {
            source = ApplyWhere(source, querySpecification.WhereClause.Expression, parameters, cteRoots);
        }

        var orderByClause = orderByOverride ?? querySpecification.OrderByClause;
        if (querySpecification.GroupByClause is not null)
        {
            var groupedSource = ApplyGroupBy(source, querySpecification.GroupByClause);
            if (querySpecification.HavingClause?.Expression is not null)
            {
                groupedSource = ApplyHaving(groupedSource, querySpecification.HavingClause.Expression, parameters);
            }

            var groupedProjection = ApplyGroupSelect(groupedSource, querySpecification.SelectClause);
            if (querySpecification.SelectClause.IsDistinct)
            {
                groupedProjection = ApplyDistinct(groupedProjection);
            }

            if (orderByClause is not null)
            {
                var orderedGroupedSource = ApplyOrderBy(CreateProjectionSource(groupedProjection), orderByClause, parameters);
                groupedProjection = orderedGroupedSource.Query;
            }

            if (querySpecification.SelectClause.Top is not null)
            {
                groupedProjection = ApplyTop(groupedProjection, querySpecification.SelectClause.Top, parameters);
            }

            return groupedProjection;
        }

        if (querySpecification.HavingClause is not null)
        {
            throw new TdsQueryEngineException("HAVING requires GROUP BY.");
        }

        if (orderByClause is not null)
        {
            source = ApplyOrderBy(source, orderByClause, parameters);
        }

        var query = ApplySelect(source, querySpecification.SelectClause);
        if (querySpecification.SelectClause.IsDistinct)
        {
            query = ApplyDistinct(query);
        }

        if (querySpecification.SelectClause.Top is not null)
        {
            query = ApplyTop(query, querySpecification.SelectClause.Top, parameters);
        }

        return query;
    }

    private QuerySource BuildSource(SqlFromClause? fromClause, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        if (fromClause is null || fromClause.TableExpressions.Count != 1)
        {
            throw new TdsQueryEngineException("The SQL query must contain exactly one FROM table expression.");
        }

        return BuildSource(fromClause.TableExpressions[0], cteRoots, parameters);
    }

    private QuerySource BuildSource(SqlTableExpression tableExpression, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        if (tableExpression is SqlTableRefExpression tableRefExpression)
        {
            return BuildTableSource(tableRefExpression, cteRoots);
        }

        if (tableExpression is SqlDerivedTableExpression derivedTableExpression)
        {
            return BuildDerivedTableSource(derivedTableExpression, cteRoots, parameters);
        }

        if (tableExpression is SqlQualifiedJoinTableExpression joinExpression)
        {
            return BuildJoinSource(joinExpression, cteRoots, parameters);
        }

        if (tableExpression is SqlUnqualifiedJoinTableExpression unqualifiedJoinExpression)
        {
            return BuildUnqualifiedJoinSource(unqualifiedJoinExpression, cteRoots, parameters);
        }

        throw new TdsQueryEngineException("Only table references, derived tables, INNER JOIN/LEFT JOIN/RIGHT JOIN table expressions, and CROSS APPLY table expressions are supported.");
    }

    private QuerySource BuildTableSource(SqlTableRefExpression tableExpression, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        var tableName = tableExpression.ObjectIdentifier.ObjectName.Value;
        var root = cteRoots.TryGetValue(tableName, out var cteRoot)
            ? cteRoot
            : _options.QueryRoots.FirstOrDefault(candidate => string.Equals(candidate.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            throw new TdsQueryEngineException($"Unknown query root '{tableName}'.");
        }

        var alias = tableExpression.Alias?.Value ?? tableName;
        return new QuerySource(
            root.Query,
            root.ElementType,
            new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase)
            {
                [alias] = new AliasBinding(root.ElementType, expression => expression),
            });
    }

    private QuerySource BuildDerivedTableSource(SqlDerivedTableExpression tableExpression, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        if (tableExpression.Alias is null)
        {
            throw new TdsQueryEngineException("Derived tables must have an alias.");
        }

        var query = TranslateQueryExpression(tableExpression.QueryExpression, parameters, cteRoots);
        if (tableExpression.ColumnList is not null && tableExpression.ColumnList.Count > 0)
        {
            query = ApplyColumnList(query, tableExpression.ColumnList, scopeName: "Derived table");
        }

        var alias = tableExpression.Alias.Value;
        return new QuerySource(
            query,
            query.ElementType,
            new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase)
            {
                [alias] = new AliasBinding(query.ElementType, expression => expression),
            });
    }

    private QuerySource BuildJoinSource(SqlQualifiedJoinTableExpression joinExpression, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var isLeftJoin = joinExpression.JoinOperator == SqlJoinOperatorType.LeftOuterJoin;
        var isRightJoin = joinExpression.JoinOperator == SqlJoinOperatorType.RightOuterJoin;
        if (joinExpression.JoinOperator != SqlJoinOperatorType.InnerJoin && !isLeftJoin && !isRightJoin)
        {
            throw new TdsQueryEngineException("Only INNER JOIN, LEFT JOIN, and RIGHT JOIN are supported.");
        }

#if !NET10_0_OR_GREATER
        if (isLeftJoin || isRightJoin)
        {
            throw new TdsQueryEngineException("LEFT JOIN and RIGHT JOIN require .NET 10 or later.");
        }
#endif

        var left = BuildSource(joinExpression.Left, cteRoots, parameters);
        var right = BuildSource(joinExpression.Right, cteRoots, parameters);
        if (joinExpression.OnClause?.Expression is not SqlComparisonBooleanExpression { ComparisonOperator: SqlComparisonBooleanExpressionType.Equals } comparison)
        {
            var joinName = isLeftJoin ? "LEFT JOIN" : isRightJoin ? "RIGHT JOIN" : "INNER JOIN";
            throw new TdsQueryEngineException($"{joinName} requires a simple equality ON clause.");
        }

        if (!ReferencesAliases(comparison.Left, left.Aliases) && !ReferencesAliases(comparison.Right, left.Aliases))
        {
            var joinName = isLeftJoin ? "LEFT JOIN" : isRightJoin ? "RIGHT JOIN" : "INNER JOIN";
            throw new TdsQueryEngineException($"{joinName} must compare a column from the left source.");
        }

        var leftParameter = Expression.Parameter(left.RowType, "left");
        var rightParameter = Expression.Parameter(right.RowType, "right");
        var leftKey = ReferencesAliases(comparison.Left, left.Aliases)
            ? BuildScalar(comparison.Left, left.Aliases, leftParameter, parameters: null)
            : BuildScalar(comparison.Right, left.Aliases, leftParameter, parameters: null);
        var rightKey = ReferencesAliases(comparison.Left, right.Aliases)
            ? BuildScalar(comparison.Left, right.Aliases, rightParameter, parameters: null)
            : BuildScalar(comparison.Right, right.Aliases, rightParameter, parameters: null);

        if (leftKey.Type != rightKey.Type)
        {
            rightKey = ConvertExpression(rightKey, leftKey.Type);
        }

        var carrierMembers = left.Aliases
            .Select(alias => new TdsProjectionMember(alias.Key, alias.Value.Type))
            .Concat(right.Aliases.Select(alias => new TdsProjectionMember(alias.Key, alias.Value.Type)))
            .ToArray();
        var carrierType = TdsProjectionTypeFactory.GetCarrierType(carrierMembers);
        var bindings = new List<MemberBinding>();
        foreach (var alias in left.Aliases)
        {
            bindings.Add(Expression.Bind(carrierType.GetProperty(alias.Key)!, alias.Value.Access(leftParameter)));
        }

        foreach (var alias in right.Aliases)
        {
            bindings.Add(Expression.Bind(carrierType.GetProperty(alias.Key)!, alias.Value.Access(rightParameter)));
        }

        var resultBody = Expression.MemberInit(Expression.New(carrierType), bindings);
        var call = isLeftJoin
            ? BuildLeftJoinCall(left, right, leftKey, rightKey, leftParameter, rightParameter, carrierType, resultBody)
            : isRightJoin
                ? BuildRightJoinCall(left, right, leftKey, rightKey, leftParameter, rightParameter, carrierType, resultBody)
                : BuildInnerJoinCall(left, right, leftKey, rightKey, leftParameter, rightParameter, carrierType, resultBody);

        var aliases = new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in carrierMembers)
        {
            var isNullableAlias = (isLeftJoin && right.Aliases.ContainsKey(member.Name)) || (isRightJoin && left.Aliases.ContainsKey(member.Name));
            aliases.Add(member.Name, new AliasBinding(member.Type, expression => Expression.Property(expression, member.Name), IsNullable: isNullableAlias));
        }

        return new QuerySource(left.Query.Provider.CreateQuery(call), carrierType, aliases);
    }

    private static MethodCallExpression BuildInnerJoinCall(QuerySource left, QuerySource right, Expression leftKey, Expression rightKey, ParameterExpression leftParameter, ParameterExpression rightParameter, Type carrierType, MemberInitExpression resultBody)
    {
        return Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Join),
            [left.RowType, right.RowType, leftKey.Type, carrierType],
            left.Query.Expression,
            right.Query.Expression,
            Expression.Quote(Expression.Lambda(leftKey, leftParameter)),
            Expression.Quote(Expression.Lambda(rightKey, rightParameter)),
            Expression.Quote(Expression.Lambda(resultBody, leftParameter, rightParameter)));
    }

    private static MethodCallExpression BuildLeftJoinCall(QuerySource left, QuerySource right, Expression leftKey, Expression rightKey, ParameterExpression leftParameter, ParameterExpression rightParameter, Type carrierType, MemberInitExpression resultBody)
    {
#if NET10_0_OR_GREATER
        return Expression.Call(
            typeof(Queryable),
            nameof(Queryable.LeftJoin),
            [left.RowType, right.RowType, leftKey.Type, carrierType],
            left.Query.Expression,
            right.Query.Expression,
            Expression.Quote(Expression.Lambda(leftKey, leftParameter)),
            Expression.Quote(Expression.Lambda(rightKey, rightParameter)),
            Expression.Quote(Expression.Lambda(resultBody, leftParameter, rightParameter)));
#else
        throw new TdsQueryEngineException("LEFT JOIN requires .NET 10 or later.");
#endif
    }

    private static MethodCallExpression BuildRightJoinCall(QuerySource left, QuerySource right, Expression leftKey, Expression rightKey, ParameterExpression leftParameter, ParameterExpression rightParameter, Type carrierType, MemberInitExpression resultBody)
    {
#if NET10_0_OR_GREATER
        return Expression.Call(
            typeof(Queryable),
            nameof(Queryable.RightJoin),
            [left.RowType, right.RowType, leftKey.Type, carrierType],
            left.Query.Expression,
            right.Query.Expression,
            Expression.Quote(Expression.Lambda(leftKey, leftParameter)),
            Expression.Quote(Expression.Lambda(rightKey, rightParameter)),
            Expression.Quote(Expression.Lambda(resultBody, leftParameter, rightParameter)));
#else
        throw new TdsQueryEngineException("RIGHT JOIN requires .NET 10 or later.");
#endif
    }

    private QuerySource BuildUnqualifiedJoinSource(SqlUnqualifiedJoinTableExpression joinExpression, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        if (joinExpression.JoinOperator != SqlJoinOperatorType.CrossApply)
        {
            throw new TdsQueryEngineException("Only CROSS APPLY is supported for unqualified joins.");
        }

        var left = BuildSource(joinExpression.Left, cteRoots, parameters);
        if (joinExpression.Right is not SqlTableValuedFunctionRefExpression tableValuedFunction)
        {
            throw new TdsQueryEngineException("CROSS APPLY currently supports only table-valued function expressions.");
        }

        if (!TryGetMethodInvocation(tableValuedFunction.ObjectIdentifier, out var receiverParts, out var methodName) ||
            !string.Equals(methodName, "nodes", StringComparison.OrdinalIgnoreCase))
        {
            throw new TdsQueryEngineException("CROSS APPLY currently supports only xml.nodes(...) table-valued calls.");
        }

        if (tableValuedFunction.Arguments.Count != 1)
        {
            throw new TdsQueryEngineException("xml.nodes requires exactly one XQuery argument.");
        }

        if (tableValuedFunction.Alias is null)
        {
            throw new TdsQueryEngineException("xml.nodes CROSS APPLY requires a table alias.");
        }

        if (tableValuedFunction.ColumnList is null || tableValuedFunction.ColumnList.Count != 1)
        {
            throw new TdsQueryEngineException("xml.nodes CROSS APPLY requires exactly one output column alias.");
        }

        var rightAlias = tableValuedFunction.Alias.Value;
        if (left.Aliases.ContainsKey(rightAlias))
        {
            throw new TdsQueryEngineException($"Duplicate alias '{rightAlias}'.");
        }

        var rightColumnName = tableValuedFunction.ColumnList[0].Value;
        var rightType = TdsProjectionTypeFactory.GetProjectionType([new TdsProjectionMember(rightColumnName, typeof(SqlXmlValue))]);

        var leftParameter = Expression.Parameter(left.RowType, "left");
        var xmlExpression = BuildColumnByParts(receiverParts, left.Aliases, leftParameter);
        var xqueryExpression = BuildScalar(tableValuedFunction.Arguments[0], left.Aliases, leftParameter, parameters, targetType: typeof(string));
        var nodesCall = Expression.Call(
            typeof(TdsQueryEngineExecutor).GetMethod(nameof(XmlNodesCore), BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Convert(xmlExpression, typeof(object)),
            xqueryExpression);

        var xmlNodeParameter = Expression.Parameter(typeof(SqlXmlValue), "xmlNode");
        var rightProjection = Expression.MemberInit(
            Expression.New(rightType),
            Expression.Bind(rightType.GetProperty(rightColumnName)!, xmlNodeParameter));
        var projectedNodes = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Select),
            [typeof(SqlXmlValue), rightType],
            nodesCall,
            Expression.Lambda(rightProjection, xmlNodeParameter));

        var carrierMembers = left.Aliases
            .Select(alias => new TdsProjectionMember(alias.Key, alias.Value.Type))
            .Append(new TdsProjectionMember(rightAlias, rightType))
            .ToArray();
        var carrierType = TdsProjectionTypeFactory.GetCarrierType(carrierMembers);
        var rightParameter = Expression.Parameter(rightType, "right");
        var bindings = new List<MemberBinding>();
        foreach (var alias in left.Aliases)
        {
            bindings.Add(Expression.Bind(carrierType.GetProperty(alias.Key)!, alias.Value.Access(leftParameter)));
        }

        bindings.Add(Expression.Bind(carrierType.GetProperty(rightAlias)!, rightParameter));
        var resultBody = Expression.MemberInit(Expression.New(carrierType), bindings);
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.SelectMany),
            [left.RowType, rightType, carrierType],
            left.Query.Expression,
            Expression.Quote(Expression.Lambda(projectedNodes, leftParameter)),
            Expression.Quote(Expression.Lambda(resultBody, leftParameter, rightParameter)));

        var aliases = new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in carrierMembers)
        {
            aliases.Add(member.Name, new AliasBinding(member.Type, expression => Expression.Property(expression, member.Name)));
        }

        return new QuerySource(left.Query.Provider.CreateQuery(call), carrierType, aliases);
    }

    private QuerySource ApplyWhere(QuerySource source, SqlBooleanExpression expression, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        var parameter = Expression.Parameter(source.RowType, "row");
        var predicate = BuildBoolean(expression, source.Aliases, parameter, parameters, cteRoots);
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            [source.RowType],
            source.Query.Expression,
            Expression.Quote(Expression.Lambda(predicate, parameter)));

        return source with { Query = source.Query.Provider.CreateQuery(call) };
    }

    private QuerySource ApplyOrderBy(QuerySource source, SqlOrderByClause orderByClause, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var query = source.Query;
        var first = true;
        foreach (var item in orderByClause.Items)
        {
            var parameter = Expression.Parameter(source.RowType, "row");
            var key = BuildScalar(item.Expression, source.Aliases, parameter, parameters);
            var methodName = (first, item.SortOrder) switch
            {
                (true, SqlSortOrder.Descending) => nameof(Queryable.OrderByDescending),
                (false, SqlSortOrder.Descending) => nameof(Queryable.ThenByDescending),
                (true, _) => nameof(Queryable.OrderBy),
                _ => nameof(Queryable.ThenBy),
            };

            var call = Expression.Call(
                typeof(Queryable),
                methodName,
                [source.RowType, key.Type],
                query.Expression,
                Expression.Quote(Expression.Lambda(key, parameter)));

            query = query.Provider.CreateQuery(call);
            first = false;
        }

        if (orderByClause.OffsetFetchClause is not null)
        {
            var offset = ReadNonNegativeInt(orderByClause.OffsetFetchClause.Offset, parameters, "OFFSET");
            var skipCall = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Skip),
                [source.RowType],
                query.Expression,
                Expression.Constant(offset));
            query = query.Provider.CreateQuery(skipCall);

            if (orderByClause.OffsetFetchClause.Fetch is not null)
            {
                var fetch = ReadNonNegativeInt(orderByClause.OffsetFetchClause.Fetch, parameters, "FETCH");
                var takeCall = Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.Take),
                    [source.RowType],
                    query.Expression,
                    Expression.Constant(fetch));
                query = query.Provider.CreateQuery(takeCall);
            }
        }

        return source with { Query = query };
    }

    private static QuerySource CreateProjectionSource(IQueryable query)
    {
        return new QuerySource(
            query,
            query.ElementType,
            new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase)
            {
                ["result"] = new AliasBinding(query.ElementType, expression => expression),
            });
    }

    private static IQueryable ApplyTop(IQueryable query, SqlTopSpecification topSpecification, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        if (topSpecification.IsPercent || topSpecification.IsWithTies)
        {
            throw new TdsQueryEngineException("TOP PERCENT and TOP WITH TIES are not supported.");
        }

        var count = ReadNonNegativeInt(topSpecification.Value, parameters, "TOP");
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Take),
            [query.ElementType],
            query.Expression,
            Expression.Constant(count));

        return query.Provider.CreateQuery(call);
    }

    private static IQueryable ApplyDistinct(IQueryable query)
    {
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Distinct),
            [query.ElementType],
            query.Expression);

        return query.Provider.CreateQuery(call);
    }

    private GroupedQuerySource ApplyGroupBy(QuerySource source, SqlGroupByClause groupByClause)
    {
        if (groupByClause.HasAll || groupByClause.Option != SqlGroupByOptionType.None || groupByClause.Items.Count == 0)
        {
            throw new TdsQueryEngineException("Only simple GROUP BY expressions are supported.");
        }

        var parameter = Expression.Parameter(source.RowType, "row");
        var keyValues = new List<Expression>();
        var keyMembers = new List<TdsProjectionMember>();
        var keyAliases = new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in groupByClause.Items)
        {
            if (item is not SqlSimpleGroupByItem groupByItem)
            {
                throw new TdsQueryEngineException("Only simple GROUP BY expressions are supported.");
            }

            var keyValue = BuildScalar(groupByItem.Expression, source.Aliases, parameter, parameters: null);
            var keyName = GetColumnName(groupByItem.Expression);
            if (keyAliases.ContainsKey(keyName))
            {
                throw new TdsQueryEngineException($"Duplicate GROUP BY column '{keyName}'.");
            }

            keyValues.Add(keyValue);
            keyMembers.Add(new TdsProjectionMember(keyName, keyValue.Type));
        }

        Expression key;
        Type keyType;
        if (keyValues.Count == 1)
        {
            key = keyValues[0];
            keyType = key.Type;
            keyAliases.Add(keyMembers[0].Name, new AliasBinding(keyType, expression => expression));
        }
        else
        {
            keyType = TdsProjectionTypeFactory.GetCarrierType(keyMembers);
            var bindings = keyMembers.Select((member, index) => Expression.Bind(keyType.GetProperty(member.Name)!, keyValues[index])).ToArray();
            key = Expression.MemberInit(Expression.New(keyType), bindings);
            foreach (var member in keyMembers)
            {
                keyAliases.Add(member.Name, new AliasBinding(member.Type, expression => Expression.Property(expression, member.Name)));
            }
        }

        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.GroupBy),
            [source.RowType, keyType],
            source.Query.Expression,
            Expression.Quote(Expression.Lambda(key, parameter)));
        var query = source.Query.Provider.CreateQuery(call);

        return new GroupedQuerySource(
            query,
            query.ElementType,
            source.RowType,
            keyAliases,
            source.Aliases);
    }

    private GroupedQuerySource ApplyHaving(GroupedQuerySource source, SqlBooleanExpression expression, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var parameter = Expression.Parameter(source.GroupType, "group");
        var predicate = BuildGroupBoolean(expression, source, parameter, parameters);
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Where),
            [source.GroupType],
            source.Query.Expression,
            Expression.Quote(Expression.Lambda(predicate, parameter)));

        return source with { Query = source.Query.Provider.CreateQuery(call) };
    }

    private IQueryable ApplyGroupSelect(GroupedQuerySource source, SqlSelectClause selectClause)
    {
        var parameter = Expression.Parameter(source.GroupType, "group");
        var members = new List<TdsProjectionMember>();
        var values = new List<Expression>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectExpression in selectClause.SelectExpressions)
        {
            if (selectExpression is not SqlSelectScalarExpression scalarExpression)
            {
                throw new TdsQueryEngineException("Only scalar GROUP BY SELECT expressions are supported.");
            }

            var value = BuildGroupScalar(scalarExpression.Expression, source, parameter, parameters: null);
            var name = scalarExpression.Alias?.Value ?? GetGroupColumnName(scalarExpression.Expression);
            if (!names.Add(name))
            {
                throw new TdsQueryEngineException($"Duplicate SELECT column name '{name}'.");
            }

            members.Add(new TdsProjectionMember(name, value.Type));
            values.Add(value);
        }

        var projectionType = TdsProjectionTypeFactory.GetProjectionType(members);
        var bindings = members.Select((member, index) => Expression.Bind(projectionType.GetProperty(member.Name)!, values[index])).ToArray();
        var body = Expression.MemberInit(Expression.New(projectionType), bindings);
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [source.GroupType, projectionType],
            source.Query.Expression,
            Expression.Quote(Expression.Lambda(body, parameter)));

        return source.Query.Provider.CreateQuery(call);
    }

    private IQueryable ApplySelect(QuerySource source, SqlSelectClause selectClause)
    {
        if (selectClause.SelectExpressions.Count == 1 && selectClause.SelectExpressions[0] is SqlSelectStarExpression)
        {
            if (source.Aliases.Count != 1)
            {
                throw new TdsQueryEngineException("SELECT * is only supported for single-table queries.");
            }

            return source.Query;
        }

        var parameter = Expression.Parameter(source.RowType, "row");
        var members = new List<TdsProjectionMember>();
        var values = new List<Expression>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectExpression in selectClause.SelectExpressions)
        {
            if (selectExpression is not SqlSelectScalarExpression scalarExpression)
            {
                throw new TdsQueryEngineException("Only scalar column SELECT expressions are supported.");
            }

            var value = BuildScalar(scalarExpression.Expression, source.Aliases, parameter, parameters: null);
            var name = scalarExpression.Alias?.Value ?? GetColumnName(scalarExpression.Expression);
            if (!names.Add(name))
            {
                throw new TdsQueryEngineException($"Duplicate SELECT column name '{name}'.");
            }

            members.Add(new TdsProjectionMember(name, value.Type));
            values.Add(value);
        }

        var projectionType = TdsProjectionTypeFactory.GetProjectionType(members);
        var bindings = members.Select((member, index) => Expression.Bind(projectionType.GetProperty(member.Name)!, values[index])).ToArray();
        var body = Expression.MemberInit(Expression.New(projectionType), bindings);
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [source.RowType, projectionType],
            source.Query.Expression,
            Expression.Quote(Expression.Lambda(body, parameter)));

        return source.Query.Provider.CreateQuery(call);
    }

    private Expression BuildBoolean(SqlBooleanExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        return expression switch
        {
            SqlComparisonBooleanExpression comparison => BuildComparison(comparison, aliases, parameter, parameters),
            SqlInBooleanExpression inExpression => BuildInBoolean(inExpression, aliases, parameter, parameters, cteRoots),
            SqlIsNullBooleanExpression isNullExpression => BuildIsNullBoolean(isNullExpression, aliases, parameter, parameters),
            SqlExistsBooleanExpression existsExpression => BuildExistsBoolean(existsExpression, aliases, parameter, parameters, cteRoots),
            SqlNotBooleanExpression notExpression => Expression.Not(BuildBoolean(notExpression.Expression, aliases, parameter, parameters, cteRoots)),
            SqlBinaryBooleanExpression { Operator: SqlBooleanOperatorType.And } binary => Expression.AndAlso(
                BuildBoolean(binary.Left, aliases, parameter, parameters, cteRoots),
                BuildBoolean(binary.Right, aliases, parameter, parameters, cteRoots)),
            SqlBinaryBooleanExpression { Operator: SqlBooleanOperatorType.Or } orBinary => Expression.OrElse(
                BuildBoolean(orBinary.Left, aliases, parameter, parameters, cteRoots),
                BuildBoolean(orBinary.Right, aliases, parameter, parameters, cteRoots)),
            _ => throw new TdsQueryEngineException("Only comparison, IN, EXISTS, and IS NULL predicates combined with AND/OR/NOT are supported."),
        };
    }

    private BinaryExpression BuildComparison(SqlComparisonBooleanExpression comparison, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var left = BuildScalar(comparison.Left, aliases, parameter, parameters);
        var right = BuildScalar(comparison.Right, aliases, parameter, parameters, left.Type);
        if (left.Type != right.Type)
        {
            right = ConvertExpression(right, left.Type);
        }

        return comparison.ComparisonOperator switch
        {
            SqlComparisonBooleanExpressionType.Equals => Expression.Equal(left, right),
            SqlComparisonBooleanExpressionType.NotEqual => Expression.NotEqual(left, right),
            SqlComparisonBooleanExpressionType.LessOrGreaterThan => Expression.NotEqual(left, right),
            SqlComparisonBooleanExpressionType.GreaterThan => Expression.GreaterThan(left, right),
            SqlComparisonBooleanExpressionType.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, right),
            SqlComparisonBooleanExpressionType.LessThan => Expression.LessThan(left, right),
            SqlComparisonBooleanExpressionType.LessThanOrEqual => Expression.LessThanOrEqual(left, right),
            _ => throw new TdsQueryEngineException($"Comparison operator '{comparison.ComparisonOperator}' is not supported."),
        };
    }

    private Expression BuildInBoolean(SqlInBooleanExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        var inExpression = BuildScalar(expression.InExpression, aliases, parameter, parameters);
        Expression containsExpression = expression.ComparisonValue switch
        {
            SqlInBooleanExpressionCollectionValue collectionValue => BuildInCollectionExpression(collectionValue, inExpression, aliases, parameter, parameters),
            SqlInBooleanExpressionQueryValue queryValue => BuildInSubqueryExpression(queryValue, inExpression, aliases, parameter, parameters, cteRoots),
            _ => throw new TdsQueryEngineException("Only IN collections and simple IN subqueries are supported."),
        };

        return expression.HasNot ? Expression.Not(containsExpression) : containsExpression;
    }

    private MethodCallExpression BuildInCollectionExpression(SqlInBooleanExpressionCollectionValue collectionValue, Expression inExpression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var values = collectionValue.Values
            .Select(value => BuildScalar(value, aliases, parameter, parameters, inExpression.Type))
            .Select(value => value.Type == inExpression.Type ? value : ConvertExpression(value, inExpression.Type));
        var valuesArray = Expression.NewArrayInit(inExpression.Type, values);

        return Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [inExpression.Type],
            valuesArray,
            inExpression);
    }

    private MethodCallExpression BuildInSubqueryExpression(SqlInBooleanExpressionQueryValue queryValue, Expression inExpression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        var subquery = TranslateInSubquery(queryValue.Value, inExpression.Type, aliases, parameter, parameters, cteRoots);
        return Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Contains),
            [inExpression.Type],
            subquery.Expression,
            inExpression);
    }

    private MethodCallExpression BuildExistsBoolean(SqlExistsBooleanExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        var subquery = TranslateExistsSubquery(expression.QueryExpression, aliases, parameter, parameters, cteRoots);
        return Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Any),
            [subquery.ElementType],
            subquery.Expression);
    }

    private Expression BuildIsNullBoolean(SqlIsNullBooleanExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var value = BuildScalar(expression.Expression, aliases, parameter, parameters);
        if (value.Type.IsValueType && Nullable.GetUnderlyingType(value.Type) is null)
        {
            return Expression.Constant(expression.HasNot, typeof(bool));
        }

        var nullExpression = Expression.Constant(null, value.Type);
        var isNullExpression = Expression.Equal(value, nullExpression);
        return expression.HasNot ? Expression.Not(isNullExpression) : isNullExpression;
    }

    private IQueryable TranslateInSubquery(SqlQueryExpression queryExpression, Type targetType, IReadOnlyDictionary<string, AliasBinding> outerAliases, ParameterExpression outerParameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        if (queryExpression is not SqlQuerySpecification querySpecification)
        {
            throw new TdsQueryEngineException("Only simple query specifications are supported in IN subqueries.");
        }

        if (querySpecification.SelectClause.IsDistinct ||
            querySpecification.SelectClause.Top is not null ||
            querySpecification.IntoClause is not null ||
            querySpecification.WindowClause is not null ||
            querySpecification.ForClause is not null ||
            querySpecification.GroupByClause is not null ||
            querySpecification.HavingClause is not null ||
            querySpecification.OrderByClause is not null)
        {
            throw new TdsQueryEngineException("The IN subquery uses a SELECT feature that is not supported.");
        }

        if (querySpecification.SelectClause.SelectExpressions.Count != 1 ||
            querySpecification.SelectClause.SelectExpressions[0] is not SqlSelectScalarExpression selectExpression)
        {
            throw new TdsQueryEngineException("The IN subquery must select exactly one scalar expression.");
        }

        var source = BuildSource(querySpecification.FromClause, cteRoots, parameters);
        var parameter = Expression.Parameter(source.RowType, GetUniqueParameterName(GetTypeParameterName(source.RowType), outerParameter.Name));
        var scopedAliases = CreateScopedAliases(source.Aliases, outerAliases, outerParameter);
        if (querySpecification.WhereClause?.Expression is not null)
        {
            var predicate = BuildBoolean(querySpecification.WhereClause.Expression, scopedAliases, parameter, parameters, cteRoots);
            var whereCall = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [source.RowType],
                source.Query.Expression,
                Expression.Quote(Expression.Lambda(predicate, parameter)));

            source = source with { Query = source.Query.Provider.CreateQuery(whereCall) };
        }

        var value = BuildScalar(selectExpression.Expression, scopedAliases, parameter, parameters, targetType);
        if (value.Type != targetType)
        {
            value = ConvertExpression(value, targetType);
        }

        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [source.RowType, targetType],
            source.Query.Expression,
            Expression.Quote(Expression.Lambda(value, parameter)));

        return source.Query.Provider.CreateQuery(call);
    }

    private IQueryable TranslateExistsSubquery(SqlQueryExpression queryExpression, IReadOnlyDictionary<string, AliasBinding> outerAliases, ParameterExpression outerParameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots)
    {
        if (queryExpression is not SqlQuerySpecification querySpecification)
        {
            throw new TdsQueryEngineException("Only simple query specifications are supported in EXISTS subqueries.");
        }

        if (querySpecification.IntoClause is not null ||
            querySpecification.WindowClause is not null ||
            querySpecification.ForClause is not null ||
            querySpecification.GroupByClause is not null ||
            querySpecification.HavingClause is not null)
        {
            throw new TdsQueryEngineException("The EXISTS subquery uses a SELECT feature that is not supported.");
        }

        var source = BuildSource(querySpecification.FromClause, cteRoots, parameters);
        var parameter = Expression.Parameter(source.RowType, GetUniqueParameterName(GetTypeParameterName(source.RowType), outerParameter.Name));
        var scopedAliases = CreateScopedAliases(source.Aliases, outerAliases, outerParameter);
        if (querySpecification.WhereClause?.Expression is not null)
        {
            var predicate = BuildBoolean(querySpecification.WhereClause.Expression, scopedAliases, parameter, parameters, cteRoots);
            var whereCall = Expression.Call(
                typeof(Queryable),
                nameof(Queryable.Where),
                [source.RowType],
                source.Query.Expression,
                Expression.Quote(Expression.Lambda(predicate, parameter)));

            source = source with { Query = source.Query.Provider.CreateQuery(whereCall) };
        }

        return source.Query;
    }

    private static IReadOnlyDictionary<string, AliasBinding> CreateScopedAliases(IReadOnlyDictionary<string, AliasBinding> sourceAliases, IReadOnlyDictionary<string, AliasBinding> outerAliases, ParameterExpression outerParameter)
    {
        var aliases = new Dictionary<string, AliasBinding>(sourceAliases, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, binding) in outerAliases)
        {
            if (aliases.ContainsKey(name))
            {
                continue;
            }

            aliases[name] = new AliasBinding(binding.Type, _ => binding.Access(outerParameter), IsOuter: true);
        }

        return aliases;
    }

    private static string GetTypeParameterName(Type type)
    {
        var name = type.Name;
        var genericMarkerIndex = name.IndexOf('`', StringComparison.Ordinal);
        if (genericMarkerIndex >= 0)
        {
            name = name[..genericMarkerIndex];
        }

        if (name.StartsWith("TdsProjection", StringComparison.Ordinal))
        {
            return "projection";
        }

        if (name.StartsWith("TdsCarrier", StringComparison.Ordinal))
        {
            return "carrier";
        }

        if (string.Equals(name, "IGrouping", StringComparison.Ordinal))
        {
            return "group";
        }

        return name.Length == 0 ? "value" : char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static string GetUniqueParameterName(string baseName, params string?[] usedNames)
    {
        var name = baseName;
        var index = 2;
        while (usedNames.Any(usedName => string.Equals(usedName, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = baseName + index.ToString(CultureInfo.InvariantCulture);
            index++;
        }

        return name;
    }

    private Expression BuildGroupBoolean(SqlBooleanExpression expression, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        return expression switch
        {
            SqlComparisonBooleanExpression comparison => BuildGroupComparison(comparison, source, parameter, parameters),
            SqlIsNullBooleanExpression isNullExpression => BuildGroupIsNullBoolean(isNullExpression, source, parameter, parameters),
            SqlBinaryBooleanExpression { Operator: SqlBooleanOperatorType.And } binary => Expression.AndAlso(
                BuildGroupBoolean(binary.Left, source, parameter, parameters),
                BuildGroupBoolean(binary.Right, source, parameter, parameters)),
            SqlBinaryBooleanExpression { Operator: SqlBooleanOperatorType.Or } orBinary => Expression.OrElse(
                BuildGroupBoolean(orBinary.Left, source, parameter, parameters),
                BuildGroupBoolean(orBinary.Right, source, parameter, parameters)),
            _ => throw new TdsQueryEngineException("Only GROUP BY comparison and IS NULL predicates combined with AND/OR are supported."),
        };
    }

    private Expression BuildGroupIsNullBoolean(SqlIsNullBooleanExpression expression, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        var value = BuildGroupScalar(expression.Expression, source, parameter, parameters);
        if (value.Type.IsValueType && Nullable.GetUnderlyingType(value.Type) is null)
        {
            return Expression.Constant(expression.HasNot, typeof(bool));
        }

        var nullExpression = Expression.Constant(null, value.Type);
        var isNullExpression = Expression.Equal(value, nullExpression);
        return expression.HasNot ? Expression.Not(isNullExpression) : isNullExpression;
    }

    private BinaryExpression BuildGroupComparison(SqlComparisonBooleanExpression comparison, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        var left = BuildGroupScalar(comparison.Left, source, parameter, parameters);
        var right = BuildGroupScalar(comparison.Right, source, parameter, parameters, left.Type);
        if (left.Type != right.Type)
        {
            right = ConvertExpression(right, left.Type);
        }

        return comparison.ComparisonOperator switch
        {
            SqlComparisonBooleanExpressionType.Equals => Expression.Equal(left, right),
            SqlComparisonBooleanExpressionType.NotEqual => Expression.NotEqual(left, right),
            SqlComparisonBooleanExpressionType.LessOrGreaterThan => Expression.NotEqual(left, right),
            SqlComparisonBooleanExpressionType.GreaterThan => Expression.GreaterThan(left, right),
            SqlComparisonBooleanExpressionType.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, right),
            SqlComparisonBooleanExpressionType.LessThan => Expression.LessThan(left, right),
            SqlComparisonBooleanExpressionType.LessThanOrEqual => Expression.LessThanOrEqual(left, right),
            _ => throw new TdsQueryEngineException($"HAVING comparison operator '{comparison.ComparisonOperator}' is not supported."),
        };
    }

    private Expression BuildGroupScalar(SqlScalarExpression expression, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Type? targetType = null)
    {
        switch (expression)
        {
            case SqlScalarRefExpression column:
                var columnName = GetColumnName(column);
                if (!source.Keys.TryGetValue(columnName, out var keyBinding))
                {
                    throw new TdsQueryEngineException($"Column '{columnName}' must appear in GROUP BY or be used in an aggregate function.");
                }

                return keyBinding.Access(Expression.Property(parameter, nameof(IGrouping<object, object>.Key)));

            case SqlAggregateFunctionCallExpression aggregateFunction:
                return BuildAggregateFunction(aggregateFunction, source, parameter);

            case SqlLiteralExpression literal:
                return BuildLiteralExpression(literal, targetType);

            case SqlScalarVariableRefExpression variable:
                if (parameters is null)
                {
                    break;
                }

                if (!parameters.TryGetValue(NormalizeName(variable.VariableName), out var queryParameter))
                {
                    throw new TdsQueryEngineException($"Missing SQL parameter '{variable.VariableName}'.");
                }

                var variableType = targetType ?? queryParameter.Value.GetType();
                return Expression.Constant(ConvertValue(queryParameter.Value, variableType), variableType);
        }

        throw new TdsQueryEngineException("Only GROUP BY columns, aggregate functions, literals, and SQL parameters are supported in grouped expressions.");
    }

    private MethodCallExpression BuildAggregateFunction(SqlAggregateFunctionCallExpression aggregateFunction, GroupedQuerySource source, ParameterExpression parameter)
    {
        if (string.Equals(aggregateFunction.FunctionName, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            if (!aggregateFunction.IsStar || aggregateFunction.ArgCount != 0)
            {
                throw new TdsQueryEngineException("Only COUNT(*) aggregate is supported.");
            }

            return Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Count),
                [source.SourceRowType],
                parameter);
        }

        return aggregateFunction.FunctionName.ToUpperInvariant() switch
        {
            "SUM" => BuildAggregateFunctionWithSelector(aggregateFunction, source, parameter, nameof(Enumerable.Sum)),
            "MIN" => BuildAggregateFunctionWithSelector(aggregateFunction, source, parameter, nameof(Enumerable.Min)),
            "MAX" => BuildAggregateFunctionWithSelector(aggregateFunction, source, parameter, nameof(Enumerable.Max)),
            "AVG" => BuildAggregateFunctionWithSelector(aggregateFunction, source, parameter, nameof(Enumerable.Average)),
            _ => throw new TdsQueryEngineException($"Aggregate function '{aggregateFunction.FunctionName}' is not supported."),
        };
    }

    private MethodCallExpression BuildAggregateFunctionWithSelector(SqlAggregateFunctionCallExpression aggregateFunction, GroupedQuerySource source, ParameterExpression parameter, string methodName)
    {
        if (aggregateFunction.IsStar || aggregateFunction.ArgCount != 1 || aggregateFunction.Arguments.SingleOrDefault() is not SqlScalarExpression argumentExpression)
        {
            throw new TdsQueryEngineException($"Aggregate function '{aggregateFunction.FunctionName}' requires exactly one column argument.");
        }

        var rowParameter = Expression.Parameter(source.SourceRowType, "row");
        var selectorBody = BuildScalar(argumentExpression, source.Aliases, rowParameter, parameters: null);
        var selector = Expression.Lambda(selectorBody, rowParameter);
        var aggregateMethod = ResolveEnumerableAggregateMethod(methodName, source.SourceRowType, selectorBody.Type);

        return Expression.Call(aggregateMethod, parameter, selector);
    }

    private static MethodInfo ResolveEnumerableAggregateMethod(string methodName, Type sourceType, Type selectorType)
    {
        var sourceSequenceType = typeof(IEnumerable<>).MakeGenericType(sourceType);
        var selectorTypeDefinition = typeof(Func<,>).MakeGenericType(sourceType, selectorType);
        var methods = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal) && method.IsGenericMethodDefinition && method.GetParameters().Length == 2)
            .OrderBy(method => method.GetGenericArguments().Length);
        foreach (var method in methods)
        {
            MethodInfo closedMethod;
            switch (method.GetGenericArguments().Length)
            {
                case 1:
                    closedMethod = method.MakeGenericMethod(sourceType);
                    break;
                case 2:
                    closedMethod = method.MakeGenericMethod(sourceType, selectorType);
                    break;
                default:
                    continue;
            }

            var parameters = closedMethod.GetParameters();
            if (parameters[0].ParameterType == sourceSequenceType && parameters[1].ParameterType == selectorTypeDefinition)
            {
                return closedMethod;
            }
        }

        throw new TdsQueryEngineException($"Aggregate function '{methodName}' does not support values of type '{selectorType.Name}'.");
    }

    private Expression BuildScalar(SqlScalarExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Type? targetType = null)
    {
        if (TryBuildScalar(expression, aliases, parameter, parameters, out var result, targetType))
        {
            return result;
        }

        throw new TdsQueryEngineException("Only column references, literals, SQL parameters, scalar functions, and arithmetic expressions are supported in expressions.");
    }

    private bool TryBuildScalar(SqlScalarExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, out Expression result, Type? targetType = null)
    {
        switch (expression)
        {
            case SqlScalarRefExpression column:
                result = BuildColumn(column, aliases, parameter);
                return true;

            case SqlLiteralExpression literal:
                result = BuildLiteralExpression(literal, targetType);
                return true;

            case SqlBinaryScalarExpression binary:
                result = BuildBinaryScalar(binary, aliases, parameter, parameters, targetType);
                return true;

            case SqlUnaryScalarExpression unary:
                result = BuildUnaryScalar(unary, aliases, parameter, parameters, targetType);
                return true;

            case SqlNullScalarExpression nullFunction:
                result = BuildNullScalarFunction(nullFunction, aliases, parameter, parameters);
                return true;

            case SqlScalarFunctionCallExpression scalarFunction:
                result = BuildScalarFunction(scalarFunction, aliases, parameter, parameters, targetType);
                return true;

            case SqlScalarVariableRefExpression variable:
                if (parameters is null)
                {
                    result = default!;
                    return false;
                }

                if (!parameters.TryGetValue(NormalizeName(variable.VariableName), out var queryParameter))
                {
                    throw new TdsQueryEngineException($"Missing SQL parameter '{variable.VariableName}'.");
                }

                var variableType = targetType ?? queryParameter.Value.GetType();
                result = Expression.Constant(ConvertValue(queryParameter.Value, variableType), variableType);
                return true;

            default:
                result = default!;
                return false;
        }
    }

    private Expression BuildBinaryScalar(SqlBinaryScalarExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Type? targetType)
    {
        var left = BuildScalar(expression.Left, aliases, parameter, parameters);
        var right = BuildScalar(expression.Right, aliases, parameter, parameters);
        Expression result = expression.Operator switch
        {
            SqlBinaryScalarOperatorType.Add when IsStringType(left.Type) || IsStringType(right.Type) => BuildStringConcatenation(left, right),
            SqlBinaryScalarOperatorType.DoublePipe => BuildStringConcatenation(left, right),
            SqlBinaryScalarOperatorType.Add => BuildArithmeticBinary(left, right, Expression.Add),
            SqlBinaryScalarOperatorType.Subtract => BuildArithmeticBinary(left, right, Expression.Subtract),
            SqlBinaryScalarOperatorType.Multiply => BuildArithmeticBinary(left, right, Expression.Multiply),
            SqlBinaryScalarOperatorType.Divide => BuildArithmeticBinary(left, right, Expression.Divide),
            SqlBinaryScalarOperatorType.Modulus => BuildArithmeticBinary(left, right, Expression.Modulo),
            _ => throw new TdsQueryEngineException($"Scalar operator '{expression.Operator}' is not supported."),
        };

        if (targetType is not null && result.Type != targetType)
        {
            result = ConvertExpression(result, targetType);
        }

        return result;
    }

    private Expression BuildUnaryScalar(SqlUnaryScalarExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Type? targetType)
    {
        var value = BuildScalar(expression.Expression, aliases, parameter, parameters);
        Expression result = expression.Operator switch
        {
            SqlUnaryScalarOperatorType.Positive => value,
            SqlUnaryScalarOperatorType.Negative => Expression.Negate(value),
            _ => throw new TdsQueryEngineException($"Unary scalar operator '{expression.Operator}' is not supported."),
        };

        if (targetType is not null && result.Type != targetType)
        {
            result = ConvertExpression(result, targetType);
        }

        return result;
    }

    private Expression BuildScalarFunction(SqlScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Type? targetType)
    {
        Expression result = function switch
        {
            SqlConvertExpression convert => BuildConvertScalarFunction(convert, aliases, parameter, parameters),
            SqlCastExpression cast => BuildCastScalarFunction(cast, aliases, parameter, parameters),
            SqlBuiltinScalarFunctionCallExpression builtin => BuildBuiltinScalarFunction(builtin, aliases, parameter, parameters),
            SqlUserDefinedScalarFunctionCallExpression userDefined => BuildUserDefinedScalarFunction(userDefined, aliases, parameter, parameters),
            _ => throw new TdsQueryEngineException($"Scalar function '{function.GetType().Name}' is not supported."),
        };

        if (targetType is not null && result.Type != targetType)
        {
            result = ConvertExpression(result, targetType);
        }

        return result;
    }

    private Expression BuildUserDefinedScalarFunction(SqlUserDefinedScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (!TryGetMethodInvocation(function.ObjectIdentifier, out var receiverParts, out var methodName))
        {
            throw new TdsQueryEngineException($"Scalar function '{function.ObjectIdentifier.Sql}' is not supported.");
        }

        var xmlExpression = BuildColumnByParts(receiverParts, aliases, parameter);
        return methodName.ToUpperInvariant() switch
        {
            "QUERY" => BuildXmlQueryMethod(function, aliases, parameter, parameters, xmlExpression),
            "VALUE" => BuildXmlValueMethod(function, aliases, parameter, parameters, xmlExpression),
            "EXIST" => BuildXmlExistMethod(function, aliases, parameter, parameters, xmlExpression),
            _ => throw new TdsQueryEngineException($"Scalar function '{function.ObjectIdentifier.Sql}' is not supported."),
        };
    }

    private Expression BuildXmlQueryMethod(SqlUserDefinedScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Expression xmlExpression)
    {
        if (function.Arguments.Count != 1)
        {
            throw new TdsQueryEngineException("xml.query requires exactly one XQuery argument.");
        }

        var xqueryExpression = BuildScalar(function.Arguments[0], aliases, parameter, parameters, targetType: typeof(string));
        return Expression.Call(
            typeof(TdsQueryEngineExecutor).GetMethod(nameof(XmlQueryCore), BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Convert(xmlExpression, typeof(object)),
            xqueryExpression);
    }

    private Expression BuildXmlValueMethod(SqlUserDefinedScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Expression xmlExpression)
    {
        if (function.Arguments.Count != 2)
        {
            throw new TdsQueryEngineException("xml.value requires an XQuery argument and a SQL type argument.");
        }

        var xqueryExpression = BuildScalar(function.Arguments[0], aliases, parameter, parameters, targetType: typeof(string));
        if (function.Arguments[1] is not SqlLiteralExpression { Type: LiteralValueType.String or LiteralValueType.UnicodeString } typeLiteral)
        {
            throw new TdsQueryEngineException("xml.value requires a string SQL type literal as second argument.");
        }

        var targetType = ResolveSqlTypeFromText(typeLiteral.Value);
        var call = Expression.Call(
            typeof(TdsQueryEngineExecutor).GetMethod(nameof(XmlValueCore), BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Convert(xmlExpression, typeof(object)),
            xqueryExpression,
            Expression.Constant(targetType, typeof(Type)));

        return targetType == typeof(SqlXmlValue) ? Expression.Convert(call, typeof(SqlXmlValue)) : Expression.Convert(call, targetType);
    }

    private Expression BuildXmlExistMethod(SqlUserDefinedScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Expression xmlExpression)
    {
        if (function.Arguments.Count != 1)
        {
            throw new TdsQueryEngineException("xml.exist requires exactly one XQuery argument.");
        }

        var xqueryExpression = BuildScalar(function.Arguments[0], aliases, parameter, parameters, targetType: typeof(string));
        return Expression.Call(
            typeof(TdsQueryEngineExecutor).GetMethod(nameof(XmlExistCore), BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Convert(xmlExpression, typeof(object)),
            xqueryExpression);
    }

    private Expression BuildBuiltinScalarFunction(SqlBuiltinScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (function.IsStar)
        {
            throw new TdsQueryEngineException($"Scalar function '{function.FunctionName}' does not support '*'.");
        }

        var functionName = function.FunctionName.ToUpperInvariant();
        if (functionName == "DATEADD")
        {
            return BuildDateAddScalarFunction(function, aliases, parameter, parameters);
        }

        if (functionName == "DATEDIFF")
        {
            return BuildDateDiffScalarFunction(function, aliases, parameter, parameters);
        }

        var arguments = function.Arguments?.Select(argument => BuildScalar(argument, aliases, parameter, parameters)).ToArray() ?? [];
        if (!_options.ScalarFunctions.TryGetValue(function.FunctionName, out var mapping))
        {
            throw new TdsQueryEngineException($"Scalar function '{function.FunctionName}' is not supported.");
        }

        return mapping(arguments);
    }

    private Expression BuildCastScalarFunction(SqlCastExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        var functionName = function.FunctionName.ToUpperInvariant();
        if (functionName is not ("CAST" or "TRY_CAST"))
        {
            throw new TdsQueryEngineException($"Scalar function '{function.FunctionName}' is not supported.");
        }

        if (function.IsStar || function.ArgCount != 1 || function.Arguments.SingleOrDefault() is not SqlScalarExpression valueExpression)
        {
            throw new TdsQueryEngineException($"Scalar function '{function.FunctionName}' requires exactly one argument.");
        }

        var value = BuildScalar(valueExpression, aliases, parameter, parameters);
        if (IsXmlDataType(function.DataTypeSpec))
        {
            var useTryConvertForXml = functionName == "TRY_CAST";
            return BuildXmlTypeConversion(value, function.DataTypeSpec, useTryConvertForXml);
        }

        var targetType = GetClrType(function.DataTypeSpec);
        var useTryConvert = functionName == "TRY_CAST";
        return BuildTypeConversion(value, targetType, useTryConvert);
    }

    private Expression BuildConvertScalarFunction(SqlConvertExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (function.IsStar || function.ArgCount is < 1 or > 2 || function.Arguments.FirstOrDefault() is not SqlScalarExpression valueExpression)
        {
            throw new TdsQueryEngineException($"Scalar function '{function.FunctionName}' requires one value argument and an optional style argument.");
        }

        var value = BuildScalar(valueExpression, aliases, parameter, parameters);
        var useTryConvert = function.FunctionName.StartsWith("TRY_", StringComparison.OrdinalIgnoreCase);
        if (IsXmlDataType(function.DataTypeSpec))
        {
            return BuildXmlTypeConversion(value, function.DataTypeSpec, useTryConvert);
        }

        var targetType = GetClrType(function.DataTypeSpec);
        return BuildTypeConversion(value, targetType, useTryConvert);
    }

    private Expression BuildNullScalarFunction(SqlNullScalarExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (!TryGetFunctionCallText(function.Xml, out var functionName, out var argumentTexts))
        {
            throw new TdsQueryEngineException("Unsupported NULL/conditional scalar expression.");
        }

        var normalizedName = functionName.ToUpperInvariant();
        return normalizedName switch
        {
            "COALESCE" => BuildCoalesceFunction(argumentTexts, aliases, parameter, parameters),
            "NULLIF" => BuildNullIfFunction(argumentTexts, aliases, parameter, parameters),
            "IIF" => BuildIifFunction(argumentTexts, aliases, parameter, parameters),
            _ => throw new TdsQueryEngineException($"Scalar function '{functionName}' is not supported."),
        };
    }

    private Expression BuildCoalesceFunction(IReadOnlyList<string> argumentTexts, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (argumentTexts.Count == 0)
        {
            throw new TdsQueryEngineException("COALESCE requires at least one argument.");
        }

        var expressions = argumentTexts.Select(argument => BuildScalar(ParseScalarExpression(argument), aliases, parameter, parameters)).ToArray();
        var result = EnsureNullableExpression(expressions[0]);
        for (var i = 1; i < expressions.Length; i++)
        {
            var right = EnsureExpressionType(EnsureNullableExpression(expressions[i]), result.Type);
            result = Expression.Coalesce(result, right);
        }

        return result;
    }

    private Expression BuildNullIfFunction(IReadOnlyList<string> argumentTexts, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (argumentTexts.Count != 2)
        {
            throw new TdsQueryEngineException("NULLIF requires exactly two arguments.");
        }

        var left = BuildScalar(ParseScalarExpression(argumentTexts[0]), aliases, parameter, parameters);
        var right = BuildScalar(ParseScalarExpression(argumentTexts[1]), aliases, parameter, parameters, left.Type);
        if (left.Type != right.Type)
        {
            right = ConvertExpression(right, left.Type);
        }

        var nullableLeft = EnsureNullableExpression(left);
        var nullableType = nullableLeft.Type;
        var nullableRight = EnsureExpressionType(EnsureNullableExpression(right), nullableType);
        return Expression.Condition(
            Expression.Equal(nullableLeft, nullableRight),
            Expression.Constant(null, nullableType),
            nullableLeft);
    }

    private Expression BuildIifFunction(IReadOnlyList<string> argumentTexts, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (argumentTexts.Count != 3)
        {
            throw new TdsQueryEngineException("IIF requires exactly three arguments.");
        }

        var conditionExpression = ParseBooleanExpression(argumentTexts[0]);
        var condition = BuildBoolean(
            conditionExpression,
            aliases,
            parameter,
            parameters ?? new Dictionary<string, TdsQueryParameter>(StringComparer.OrdinalIgnoreCase),
            cteRoots: new Dictionary<string, TdsQueryRoot>(StringComparer.OrdinalIgnoreCase));
        var ifTrue = BuildScalar(ParseScalarExpression(argumentTexts[1]), aliases, parameter, parameters);
        var ifFalse = BuildScalar(ParseScalarExpression(argumentTexts[2]), aliases, parameter, parameters, ifTrue.Type);
        if (ifTrue.Type != ifFalse.Type)
        {
            ifFalse = ConvertExpression(ifFalse, ifTrue.Type);
        }

        return Expression.Condition(condition, ifTrue, ifFalse);
    }

    private Expression BuildDateAddScalarFunction(SqlBuiltinScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (function.ArgCount != 3)
        {
            throw new TdsQueryEngineException("DATEADD requires exactly three arguments.");
        }

        var datePart = Expression.Constant(ReadDatePartName(function.Arguments[0]), typeof(string));
        var value = BuildScalar(function.Arguments[1], aliases, parameter, parameters, targetType: typeof(int));
        var dateValue = BuildScalar(function.Arguments[2], aliases, parameter, parameters, targetType: typeof(DateTime));
        return Expression.Call(
            typeof(TdsQueryEngineExecutor).GetMethod(nameof(DateAddCore), BindingFlags.NonPublic | BindingFlags.Static)!,
            datePart,
            value,
            dateValue);
    }

    private Expression BuildDateDiffScalarFunction(SqlBuiltinScalarFunctionCallExpression function, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
    {
        if (function.ArgCount != 3)
        {
            throw new TdsQueryEngineException("DATEDIFF requires exactly three arguments.");
        }

        var datePart = Expression.Constant(ReadDatePartName(function.Arguments[0]), typeof(string));
        var startDate = BuildScalar(function.Arguments[1], aliases, parameter, parameters, targetType: typeof(DateTime));
        var endDate = BuildScalar(function.Arguments[2], aliases, parameter, parameters, targetType: typeof(DateTime));
        return Expression.Call(
            typeof(TdsQueryEngineExecutor).GetMethod(nameof(DateDiffCore), BindingFlags.NonPublic | BindingFlags.Static)!,
            datePart,
            startDate,
            endDate);
    }

    private static Expression BuildArithmeticBinary(Expression left, Expression right, Func<Expression, Expression, BinaryExpression> operation)
    {
        var leftType = GetNonNullableType(left.Type);
        var rightType = GetNonNullableType(right.Type);
        if (!IsNumericType(leftType) || !IsNumericType(rightType))
        {
            throw new TdsQueryEngineException("Arithmetic operations require numeric operands.");
        }

        var targetType = GetNumericPromotionType(leftType, rightType);
        var convertedLeft = left.Type == targetType ? left : Expression.Convert(left, targetType);
        var convertedRight = right.Type == targetType ? right : Expression.Convert(right, targetType);
        return operation(convertedLeft, convertedRight);
    }

    private Expression BuildStringConcatenation(Expression left, Expression right)
    {
        return Expression.Call(
            typeof(string).GetMethod(nameof(string.Concat), [typeof(string), typeof(string)])!,
            EnsureString(left),
            EnsureString(right));
    }

    private Expression EnsureString(Expression expression)
    {
        if (expression.Type == typeof(string))
        {
            return expression;
        }

        return Expression.Call(
            typeof(Convert).GetMethod(nameof(Convert.ToString), [typeof(object), typeof(IFormatProvider)])!,
            Expression.Convert(expression, typeof(object)),
            Expression.Constant(CultureInfo.InvariantCulture));
    }

    private static bool IsStringType(Type type) => GetNonNullableType(type) == typeof(string);

    private static bool IsNumericType(Type type)
    {
        type = GetNonNullableType(type);
        return type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);
    }

    private static Type GetNonNullableType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static Type GetNumericPromotionType(Type left, Type right)
    {
        left = GetNonNullableType(left);
        right = GetNonNullableType(right);
        if (left == typeof(decimal) || right == typeof(decimal))
        {
            return typeof(decimal);
        }

        if (left == typeof(double) || right == typeof(double))
        {
            return typeof(double);
        }

        if (left == typeof(float) || right == typeof(float))
        {
            return typeof(float);
        }

        if (left == typeof(ulong) || right == typeof(ulong))
        {
            return typeof(ulong);
        }

        if (left == typeof(long) || right == typeof(long))
        {
            return typeof(long);
        }

        if (left == typeof(uint) || right == typeof(uint))
        {
            return typeof(uint);
        }

        return typeof(int);
    }

    private static bool TryGetMethodInvocation(SqlObjectIdentifier objectIdentifier, out IReadOnlyList<string> receiverParts, out string methodName)
    {
        methodName = objectIdentifier.ObjectName.Value;
        var parts = new List<string>(capacity: 3);
        if (!string.IsNullOrEmpty(objectIdentifier.ServerName?.Value))
        {
            parts.Add(objectIdentifier.ServerName.Value);
        }

        if (!string.IsNullOrEmpty(objectIdentifier.DatabaseName?.Value))
        {
            parts.Add(objectIdentifier.DatabaseName.Value);
        }

        if (!string.IsNullOrEmpty(objectIdentifier.SchemaName?.Value))
        {
            parts.Add(objectIdentifier.SchemaName.Value);
        }

        receiverParts = parts;
        return parts.Count > 0 && methodName.Length > 0;
    }

    private static Expression BuildColumnByParts(IReadOnlyList<string> parts, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter)
    {
        return parts.Count switch
        {
            1 => BuildUnqualifiedColumn(parts[0], aliases, parameter),
            2 => BuildQualifiedColumn(parts[0], parts[1], aliases, parameter),
            _ => throw new TdsQueryEngineException($"Invalid xml method target '{string.Join(".", parts)}'."),
        };
    }

    private static Expression BuildUnqualifiedColumn(string columnName, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter)
    {
        AliasBinding? binding;
        if (aliases.Count == 1)
        {
            binding = aliases.Values.Single();
        }
        else
        {
            var localBindings = aliases.Values.Where(candidate => !candidate.IsOuter).ToArray();
            var candidates = localBindings.Length == 0 ? aliases.Values : localBindings;
            binding = candidates.SingleOrDefault(candidate => FindMember(candidate.Type, columnName) is not null);
            if (binding is null)
            {
                throw new TdsQueryEngineException($"Ambiguous or unknown column '{columnName}'.");
            }
        }

        var member = FindMember(binding.Type, columnName) ?? throw new TdsQueryEngineException($"Unknown column '{columnName}'.");
        return BuildColumnAccess(binding, member, parameter);
    }

    private static Expression BuildQualifiedColumn(string alias, string columnName, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter)
    {
        if (!aliases.TryGetValue(alias, out var binding))
        {
            throw new TdsQueryEngineException($"Unknown table alias '{alias}'.");
        }

        var member = FindMember(binding.Type, columnName) ?? throw new TdsQueryEngineException($"Unknown column '{columnName}'.");
        return BuildColumnAccess(binding, member, parameter);
    }

    private static Expression BuildColumn(SqlScalarRefExpression column, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter)
    {
        var qualifier = GetQualifier(column);
        var columnName = GetColumnName(column);
        return qualifier is null
            ? BuildUnqualifiedColumn(columnName, aliases, parameter)
            : BuildQualifiedColumn(qualifier, columnName, aliases, parameter);
    }

    private static Expression BuildColumnAccess(AliasBinding binding, MemberInfo member, ParameterExpression parameter)
    {
        var instance = binding.Access(parameter);
        var memberAccess = BuildMemberAccess(instance, member);
        if (!binding.IsNullable || instance.Type.IsValueType)
        {
            return memberAccess;
        }

        var nullCheck = Expression.Equal(instance, Expression.Constant(null, instance.Type));
        if (memberAccess.Type.IsValueType && Nullable.GetUnderlyingType(memberAccess.Type) is null)
        {
            var nullableType = typeof(Nullable<>).MakeGenericType(memberAccess.Type);
            return Expression.Condition(
                nullCheck,
                Expression.Constant(null, nullableType),
                Expression.Convert(memberAccess, nullableType));
        }

        return Expression.Condition(
            nullCheck,
            Expression.Constant(null, memberAccess.Type),
            memberAccess);
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(property => property.GetIndexParameters().Length == 0 && property.CanRead && string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? (MemberInfo?)type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<MemberInfo> GetReadableMembers(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            .ToList();
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new TdsQueryEngineException($"Unsupported member '{member.Name}'."),
        };
    }

    private static Expression BuildMemberAccess(Expression expression, MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => Expression.Property(expression, property),
            FieldInfo field => Expression.Field(expression, field),
            _ => throw new TdsQueryEngineException($"Unsupported member '{member.Name}'."),
        };
    }

    private static bool ReferencesAliases(SqlScalarExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases)
    {
        if (expression is not SqlScalarRefExpression column)
        {
            return false;
        }

        var qualifier = GetQualifier(column);
        if (qualifier is not null)
        {
            return aliases.ContainsKey(qualifier);
        }

        var columnName = GetColumnName(column);
        return aliases.Values.Count(alias => FindMember(alias.Type, columnName) is not null) == 1;
    }

    private static Expression ConvertExpression(Expression expression, Type targetType)
    {
        if (expression.Type == targetType)
        {
            return expression;
        }

        return Expression.Convert(expression, targetType);
    }

    private static Expression EnsureNullableExpression(Expression expression)
    {
        if (!expression.Type.IsValueType || Nullable.GetUnderlyingType(expression.Type) is not null)
        {
            return expression;
        }

        var nullableType = typeof(Nullable<>).MakeGenericType(expression.Type);
        return Expression.Convert(expression, nullableType);
    }

    private static Expression EnsureExpressionType(Expression expression, Type targetType)
    {
        return expression.Type == targetType ? expression : ConvertExpression(expression, targetType);
    }

    private Expression BuildXmlTypeConversion(Expression value, SqlDataTypeSpecification dataTypeSpec, bool useTryConvert)
    {
        var schemaCollectionName = dataTypeSpec.XmlSchemaCollection is null
            ? null
            : NormalizeObjectIdentifier(dataTypeSpec.XmlSchemaCollection.Sql);
        XmlSchemaSet? schemaSet = null;
        if (schemaCollectionName is not null && !_options.XmlSchemaCollections.TryGetValue(schemaCollectionName, out schemaSet))
        {
            throw new TdsQueryEngineException($"Unknown XML schema collection '{schemaCollectionName}'.");
        }

        var method = typeof(TdsQueryEngineExecutor).GetMethod(
            useTryConvert ? nameof(TryConvertToXmlCore) : nameof(ConvertToXmlCore),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return Expression.Call(
            method,
            Expression.Convert(value, typeof(object)),
            Expression.Constant(schemaCollectionName, typeof(string)),
            Expression.Constant(schemaSet, typeof(XmlSchemaSet)),
            Expression.Constant(dataTypeSpec.XmlDocumentConstraint));
    }

    private static bool IsXmlDataType(SqlDataTypeSpecification dataTypeSpec)
    {
        return string.Equals(dataTypeSpec.DataType.ObjectIdentifier.ObjectName.Value, "XML", StringComparison.OrdinalIgnoreCase);
    }

    private static Expression BuildTypeConversion(Expression value, Type targetType, bool useTryConvert)
    {
        var method = typeof(TdsQueryEngineExecutor).GetMethod(
            useTryConvert ? nameof(TryConvertToTypeCore) : nameof(ConvertToTypeCore),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var converted = Expression.Call(
            method,
            Expression.Convert(value, typeof(object)),
            Expression.Constant(targetType, typeof(Type)));
        var expressionType = useTryConvert && targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
            ? typeof(Nullable<>).MakeGenericType(targetType)
            : targetType;
        return Expression.Convert(converted, expressionType);
    }

    private static object? ConvertToTypeCore(object? value, Type targetType)
    {
        return ConvertValue(value, targetType);
    }

    private static object? TryConvertToTypeCore(object? value, Type targetType)
    {
        try
        {
            return ConvertValue(value, targetType);
        }
        catch
        {
            return null;
        }
    }

    private static SqlXmlValue ConvertToXmlCore(object? value, string? schemaCollectionName, XmlSchemaSet? schemaSet, SqlXmlDocumentConstraint documentConstraint)
    {
        if (!TryConvertToSqlXmlValue(value, schemaCollectionName, out var xmlValue) || xmlValue is null)
        {
            throw new TdsQueryEngineException("Cannot convert NULL to 'XML'.");
        }

        EnsureXmlConstraint(xmlValue.Value, documentConstraint);
        if (schemaSet is not null)
        {
            ValidateXmlAgainstSchema(xmlValue.Value, schemaSet, schemaCollectionName);
        }

        return schemaCollectionName is null ? xmlValue : xmlValue with { SchemaCollection = schemaCollectionName };
    }

    private static SqlXmlValue? TryConvertToXmlCore(object? value, string? schemaCollectionName, XmlSchemaSet? schemaSet, SqlXmlDocumentConstraint documentConstraint)
    {
        try
        {
            return value is null
                ? null
                : ConvertToXmlCore(value, schemaCollectionName, schemaSet, documentConstraint);
        }
        catch
        {
            return null;
        }
    }

    private static SqlXmlValue? XmlQueryCore(object? value, string xquery)
    {
        if (!TryConvertToSqlXmlValue(value, schemaCollectionName: null, out var xmlValue) || xmlValue is null)
        {
            return null;
        }

        var navigator = CreateXmlContextNavigator(xmlValue.Value);
        var evaluation = navigator.Evaluate(xquery);
        if (evaluation is XPathNodeIterator iterator)
        {
            var fragments = new List<string>();
            while (iterator.MoveNext())
            {
                var current = iterator.Current
                    ?? throw new TdsQueryEngineException("xml.query returned an invalid sequence.");
                fragments.Add(current.OuterXml);
            }

            return new SqlXmlValue(string.Concat(fragments), xmlValue.SchemaCollection);
        }

        var scalar = Convert.ToString(evaluation, CultureInfo.InvariantCulture) ?? string.Empty;
        return new SqlXmlValue(SecurityElement.Escape(scalar) ?? string.Empty, xmlValue.SchemaCollection);
    }

    private static object XmlValueCore(object? value, string xquery, Type targetType)
    {
        if (!TryConvertToSqlXmlValue(value, schemaCollectionName: null, out var xmlValue) || xmlValue is null)
        {
            throw new TdsQueryEngineException("xml.value cannot be evaluated on NULL.");
        }

        var navigator = CreateXmlContextNavigator(xmlValue.Value);
        var evaluation = navigator.Evaluate(xquery);
        if (evaluation is XPathNodeIterator iterator)
        {
            if (!iterator.MoveNext())
            {
                throw new TdsQueryEngineException("xml.value returned an empty sequence.");
            }

            var current = iterator.Current
                ?? throw new TdsQueryEngineException("xml.value returned an invalid sequence.");
            var rawNodeValue = current.Value;
            if (targetType == typeof(SqlXmlValue))
            {
                return new SqlXmlValue(current.OuterXml, xmlValue.SchemaCollection);
            }

            return ConvertValue(rawNodeValue, targetType)
                ?? throw new TdsQueryEngineException($"xml.value returned NULL for target type '{targetType.Name}'.");
        }

        if (targetType == typeof(SqlXmlValue))
        {
            var scalar = Convert.ToString(evaluation, CultureInfo.InvariantCulture) ?? string.Empty;
            return new SqlXmlValue(SecurityElement.Escape(scalar) ?? string.Empty, xmlValue.SchemaCollection);
        }

        return ConvertValue(evaluation, targetType)
            ?? throw new TdsQueryEngineException($"xml.value returned NULL for target type '{targetType.Name}'.");
    }

    private static int? XmlExistCore(object? value, string xquery)
    {
        if (!TryConvertToSqlXmlValue(value, schemaCollectionName: null, out var xmlValue) || xmlValue is null)
        {
            return null;
        }

        var navigator = CreateXmlContextNavigator(xmlValue.Value);
        var evaluation = navigator.Evaluate(xquery);
        if (evaluation is XPathNodeIterator iterator)
        {
            return iterator.MoveNext() ? 1 : 0;
        }

        return evaluation switch
        {
            bool boolResult => boolResult ? 1 : 0,
            string textResult => string.IsNullOrEmpty(textResult) ? 0 : 1,
            _ => evaluation is null ? 0 : 1,
        };
    }

    private static IEnumerable<SqlXmlValue> XmlNodesCore(object? value, string xquery)
    {
        if (!TryConvertToSqlXmlValue(value, schemaCollectionName: null, out var xmlValue) || xmlValue is null)
        {
            return [];
        }

        var navigator = CreateXmlContextNavigator(xmlValue.Value);
        var evaluation = navigator.Evaluate(xquery);
        if (evaluation is not XPathNodeIterator iterator)
        {
            return [];
        }

        var result = new List<SqlXmlValue>();
        while (iterator.MoveNext())
        {
            var current = iterator.Current;
            if (current is null)
            {
                continue;
            }

            result.Add(new SqlXmlValue(current.OuterXml, xmlValue.SchemaCollection));
        }

        return result;
    }

    private static bool TryConvertToSqlXmlValue(object? value, string? schemaCollectionName, out SqlXmlValue? xmlValue)
    {
        if (value is null or DBNull)
        {
            xmlValue = null;
            return true;
        }

        xmlValue = value switch
        {
            SqlXmlValue typedXml => schemaCollectionName is null ? typedXml : typedXml with { SchemaCollection = schemaCollectionName },
            string text => new SqlXmlValue(text, schemaCollectionName),
            XDocument document => new SqlXmlValue(document.ToString(SaveOptions.DisableFormatting), schemaCollectionName),
            XElement element => new SqlXmlValue(element.ToString(SaveOptions.DisableFormatting), schemaCollectionName),
            XmlDocument document => new SqlXmlValue(document.OuterXml, schemaCollectionName),
            _ => null,
        };

        if (xmlValue is null)
        {
            return false;
        }

        _ = XDocument.Parse(xmlValue.Value, LoadOptions.PreserveWhitespace);
        return true;
    }

    private static void ValidateXmlAgainstSchema(string xml, XmlSchemaSet schemaSet, string? schemaCollectionName)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        string? validationError = null;
        document.Validate(schemaSet, (_, args) =>
        {
            validationError ??= args.Message;
        });

        if (validationError is not null)
        {
            throw new TdsQueryEngineException($"XML value is not valid for schema collection '{schemaCollectionName}': {validationError}");
        }
    }

    private static XPathNavigator CreateXmlContextNavigator(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        return document.Root?.CreateNavigator()
            ?? throw new TdsQueryEngineException("Cannot evaluate XML query on an empty document.");
    }

    private static void EnsureXmlConstraint(string xml, SqlXmlDocumentConstraint documentConstraint)
    {
        _ = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        if (documentConstraint == SqlXmlDocumentConstraint.None)
        {
            return;
        }

        // XDocument parsing already enforces a single root element, which matches DOCUMENT/CONTENT checks in this engine.
    }

    private static Type GetClrType(SqlDataTypeSpecification dataTypeSpec)
    {
        var typeName = dataTypeSpec.DataType.ObjectIdentifier.ObjectName.Value;
        return typeName.ToUpperInvariant() switch
        {
            "BIT" => typeof(bool),
            "TINYINT" => typeof(byte),
            "SMALLINT" => typeof(short),
            "INT" => typeof(int),
            "BIGINT" => typeof(long),
            "REAL" => typeof(float),
            "FLOAT" => typeof(double),
            "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY" => typeof(decimal),
            "CHAR" or "NCHAR" or "VARCHAR" or "NVARCHAR" or "TEXT" or "NTEXT" or "SYSNAME" => typeof(string),
            "XML" => typeof(SqlXmlValue),
            "UNIQUEIDENTIFIER" => typeof(Guid),
            "DATE" => typeof(DateOnly),
            "TIME" => typeof(TimeSpan),
            "DATETIMEOFFSET" => typeof(DateTimeOffset),
            "DATETIME" or "DATETIME2" or "SMALLDATETIME" => typeof(DateTime),
            "BINARY" or "VARBINARY" or "IMAGE" or "ROWVERSION" or "TIMESTAMP" => typeof(byte[]),
            _ => throw new TdsQueryEngineException($"SQL data type '{typeName}' is not supported."),
        };
    }

    private static bool TryGetFunctionCallText(string xml, out string functionName, out IReadOnlyList<string> argumentTexts)
    {
        functionName = string.Empty;
        argumentTexts = [];
        if (!TryExtractExpressionComment(xml, out var invocation))
        {
            return false;
        }

        var startIndex = invocation.IndexOf('(', StringComparison.Ordinal);
        var endIndex = invocation.LastIndexOf(')');
        if (startIndex <= 0 || endIndex <= startIndex)
        {
            return false;
        }

        functionName = invocation[..startIndex].Trim();
        argumentTexts = SplitTopLevelArguments(invocation[(startIndex + 1)..endIndex]);
        return !string.IsNullOrWhiteSpace(functionName);
    }

    private static bool TryExtractExpressionComment(string xml, out string expressionText)
    {
        expressionText = string.Empty;
        const string StartMarker = "<!--";
        const string EndMarker = "-->";
        var start = xml.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        start += StartMarker.Length;
        var end = xml.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        expressionText = xml[start..end].Trim();
        return expressionText.Length > 0;
    }

    private static IReadOnlyList<string> SplitTopLevelArguments(string argumentsText)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        var inString = false;
        for (var index = 0; index < argumentsText.Length; index++)
        {
            var current = argumentsText[index];
            if (current == '\'')
            {
                if (inString && index + 1 < argumentsText.Length && argumentsText[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (current == '(')
            {
                depth++;
            }
            else if (current == ')')
            {
                depth--;
            }
            else if (current == ',' && depth == 0)
            {
                result.Add(argumentsText[start..index].Trim());
                start = index + 1;
            }
        }

        result.Add(argumentsText[start..].Trim());
        return result;
    }

    private static SqlScalarExpression ParseScalarExpression(string expressionText)
    {
        var parseResult = SqlParser.Parse("SELECT " + expressionText, new SqlParserParseOptions(), out _);
        if (parseResult.Errors.Any() || parseResult.ParseErrors.Any())
        {
            throw new TdsQueryEngineException($"Invalid scalar expression '{expressionText}'.");
        }

        if (parseResult.Script.Batches.Count != 1 ||
            parseResult.Script.Batches[0].Statements.Count != 1 ||
            parseResult.Script.Batches[0].Statements[0] is not SqlSelectStatement selectStatement ||
            selectStatement.SelectSpecification.QueryExpression is not SqlQuerySpecification querySpecification ||
            querySpecification.SelectClause.SelectExpressions.Count != 1 ||
            querySpecification.SelectClause.SelectExpressions[0] is not SqlSelectScalarExpression selectExpression)
        {
            throw new TdsQueryEngineException($"Invalid scalar expression '{expressionText}'.");
        }

        return selectExpression.Expression;
    }

    private static SqlBooleanExpression ParseBooleanExpression(string expressionText)
    {
        var parseResult = SqlParser.Parse("SELECT 1 WHERE " + expressionText, new SqlParserParseOptions(), out _);
        if (parseResult.Errors.Any() || parseResult.ParseErrors.Any())
        {
            throw new TdsQueryEngineException($"Invalid boolean expression '{expressionText}'.");
        }

        if (parseResult.Script.Batches.Count != 1 ||
            parseResult.Script.Batches[0].Statements.Count != 1 ||
            parseResult.Script.Batches[0].Statements[0] is not SqlSelectStatement selectStatement ||
            selectStatement.SelectSpecification.QueryExpression is not SqlQuerySpecification querySpecification ||
            querySpecification.WhereClause?.Expression is not SqlBooleanExpression expression)
        {
            throw new TdsQueryEngineException($"Invalid boolean expression '{expressionText}'.");
        }

        return expression;
    }

    private static string ReadDatePartName(SqlScalarExpression expression)
    {
        return expression switch
        {
            SqlLiteralExpression literal when literal.Type is LiteralValueType.String or LiteralValueType.UnicodeString or LiteralValueType.Identifier => literal.Value,
            SqlColumnRefExpression column => column.ColumnName.Value,
            _ => throw new TdsQueryEngineException("DATEADD and DATEDIFF require a date part identifier as first argument."),
        };
    }

    private static DateTime DateAddCore(string datePart, int value, DateTime dateValue)
    {
        return datePart.ToUpperInvariant() switch
        {
            "YEAR" or "YY" or "YYYY" => dateValue.AddYears(value),
            "QUARTER" or "QQ" or "Q" => dateValue.AddMonths(value * 3),
            "MONTH" or "MM" or "M" => dateValue.AddMonths(value),
            "DAYOFYEAR" or "DY" or "Y" or "DAY" or "DD" or "D" => dateValue.AddDays(value),
            "WEEK" or "WK" or "WW" => dateValue.AddDays(value * 7d),
            "HOUR" or "HH" => dateValue.AddHours(value),
            "MINUTE" or "MI" or "N" => dateValue.AddMinutes(value),
            "SECOND" or "SS" or "S" => dateValue.AddSeconds(value),
            "MILLISECOND" or "MS" => dateValue.AddMilliseconds(value),
            _ => throw new TdsQueryEngineException($"DATEADD date part '{datePart}' is not supported."),
        };
    }

    private static int DateDiffCore(string datePart, DateTime startDate, DateTime endDate)
    {
        return datePart.ToUpperInvariant() switch
        {
            "YEAR" or "YY" or "YYYY" => endDate.Year - startDate.Year,
            "QUARTER" or "QQ" or "Q" => ((endDate.Year - startDate.Year) * 12 + endDate.Month - startDate.Month) / 3,
            "MONTH" or "MM" or "M" => (endDate.Year - startDate.Year) * 12 + endDate.Month - startDate.Month,
            "DAYOFYEAR" or "DY" or "Y" or "DAY" or "DD" or "D" => (int)(endDate - startDate).TotalDays,
            "WEEK" or "WK" or "WW" => (int)((endDate - startDate).TotalDays / 7d),
            "HOUR" or "HH" => (int)(endDate - startDate).TotalHours,
            "MINUTE" or "MI" or "N" => (int)(endDate - startDate).TotalMinutes,
            "SECOND" or "SS" or "S" => (int)(endDate - startDate).TotalSeconds,
            "MILLISECOND" or "MS" => (int)(endDate - startDate).TotalMilliseconds,
            _ => throw new TdsQueryEngineException($"DATEDIFF date part '{datePart}' is not supported."),
        };
    }

    private static Type ResolveSqlTypeFromText(string sqlTypeText)
    {
        var parseResult = SqlParser.Parse("SELECT CAST(NULL AS " + sqlTypeText + ")", new SqlParserParseOptions(), out _);
        if (parseResult.Errors.Any() || parseResult.ParseErrors.Any())
        {
            throw new TdsQueryEngineException($"xml.value SQL type '{sqlTypeText}' is invalid.");
        }

        if (parseResult.Script.Batches.Count != 1 ||
            parseResult.Script.Batches[0].Statements.Count != 1 ||
            parseResult.Script.Batches[0].Statements[0] is not SqlSelectStatement selectStatement ||
            selectStatement.SelectSpecification.QueryExpression is not SqlQuerySpecification querySpecification ||
            querySpecification.SelectClause.SelectExpressions.Count != 1 ||
            querySpecification.SelectClause.SelectExpressions[0] is not SqlSelectScalarExpression { Expression: SqlCastExpression castExpression })
        {
            throw new TdsQueryEngineException($"xml.value SQL type '{sqlTypeText}' is invalid.");
        }

        return GetClrType(castExpression.DataTypeSpec);
    }

    private static string NormalizeObjectIdentifier(string identifier)
    {
        var parts = identifier.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return identifier.Trim();
        }

        return string.Join(".", parts.Select(static part =>
        {
            if (part.Length >= 2 && part[0] == '[' && part[^1] == ']')
            {
                return part[1..^1];
            }

            if (part.Length >= 2 && part[0] == '"' && part[^1] == '"')
            {
                return part[1..^1];
            }

            return part;
        }));
    }

    private static object? ConvertLiteral(SqlLiteralExpression literal, Type? targetType)
    {
        object? value = literal.Type switch
        {
            LiteralValueType.Null => null,
            LiteralValueType.Integer => ConvertIntegerLiteral(literal.Value),
            LiteralValueType.Numeric or LiteralValueType.Money => decimal.Parse(literal.Value, NumberStyles.Number, CultureInfo.InvariantCulture),
            LiteralValueType.Real => double.Parse(literal.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
            LiteralValueType.String or LiteralValueType.UnicodeString or LiteralValueType.Identifier => literal.Value,
            _ => throw new TdsQueryEngineException($"Literal type '{literal.Type}' is not supported."),
        };

        return targetType is null ? value : ConvertValue(value, targetType);
    }

    private static Expression BuildLiteralExpression(SqlLiteralExpression literal, Type? targetType)
    {
        var value = ConvertLiteral(literal, targetType);
        var type = targetType ?? value?.GetType() ?? typeof(object);
        if (value is null)
        {
            return Expression.Constant(null, type);
        }

        if (type.IsInstanceOfType(value))
        {
            return Expression.Constant(value, type);
        }

        var constant = Expression.Constant(value, value.GetType());
        return constant.Type == type ? constant : ConvertExpression(constant, type);
    }

    private static int ReadNonNegativeInt(SqlScalarExpression expression, IReadOnlyDictionary<string, TdsQueryParameter> parameters, string clauseName)
    {
        object? value = expression switch
        {
            SqlLiteralExpression literal => ConvertLiteral(literal, typeof(int)),
            SqlScalarVariableRefExpression variable when parameters.TryGetValue(NormalizeName(variable.VariableName), out var parameter) => ConvertValue(parameter.Value, typeof(int)),
            SqlScalarVariableRefExpression variable => throw new TdsQueryEngineException($"Missing SQL parameter '{variable.VariableName}'."),
            _ => throw new TdsQueryEngineException($"{clauseName} requires an integer literal or SQL parameter."),
        };
        var count = value is null ? throw new TdsQueryEngineException($"{clauseName} value cannot be NULL.") : (int)value;
        if (count < 0)
        {
            throw new TdsQueryEngineException($"{clauseName} value cannot be negative.");
        }

        return count;
    }

    private static object ConvertIntegerLiteral(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
            ? (object)intValue
            : long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is DBNull)
        {
            value = null;
        }

        if (value is null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
            {
                throw new TdsQueryEngineException($"Cannot convert NULL to '{targetType.Name}'.");
            }

            return null;
        }

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            targetType = nullableType;
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (targetType == typeof(SqlXmlValue))
        {
            if (!TryConvertToSqlXmlValue(value, schemaCollectionName: null, out var sqlXmlValue) || sqlXmlValue is null)
            {
                throw new TdsQueryEngineException($"Cannot convert value of type '{value.GetType().Name}' to 'XML'.");
            }

            return sqlXmlValue;
        }

        if (value is SqlXmlValue xmlValue)
        {
            if (targetType == typeof(string))
            {
                return xmlValue.Value;
            }

            value = xmlValue.Value;
        }

        if (targetType.IsEnum)
        {
            return value is string text ? Enum.Parse(targetType, text, ignoreCase: true) : Enum.ToObject(targetType, value);
        }

        if (targetType == typeof(Guid))
        {
            return value is Guid guid ? guid : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        if (targetType == typeof(DateOnly))
        {
            return value is DateOnly date ? date : DateOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(TimeOnly))
        {
            return value is TimeOnly time ? time : TimeOnly.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static string GetCommandText(TdsQueryContext context)
    {
        if (context.RequestType == TdsQueryRequestType.SqlBatch)
        {
            return string.IsNullOrWhiteSpace(context.CommandText) ? throw new TdsQueryEngineException("The SQL batch is empty.") : context.CommandText;
        }

        var statement = context.Parameters.Count == 0 ? null : context.Parameters[0].AsString();
        return string.IsNullOrWhiteSpace(statement) ? throw new TdsQueryEngineException("sp_executesql did not provide a SQL statement.") : statement;
    }

    private static Dictionary<string, TdsQueryParameter> GetSqlParameters(TdsQueryContext context)
    {
        var result = new Dictionary<string, TdsQueryParameter>(StringComparer.OrdinalIgnoreCase);
        var skip = context.RequestType == TdsQueryRequestType.Rpc ? 2 : 0;
        foreach (var parameter in context.Parameters.Skip(skip))
        {
            result[NormalizeName(parameter.Name)] = parameter;
        }

        return result;
    }

    private static string NormalizeName(string? name)
    {
        return name?.TrimStart('@') ?? string.Empty;
    }

    private static string? GetQualifier(SqlScalarRefExpression column)
    {
        return column.MultipartIdentifier.Count > 1 ? column.MultipartIdentifier[0].Value : null;
    }

    private static string GetColumnName(SqlScalarExpression expression)
    {
        return expression is SqlScalarRefExpression column ? GetColumnName(column) : throw new TdsQueryEngineException("SELECT expression must have an alias.");
    }

    private static string GetGroupColumnName(SqlScalarExpression expression)
    {
        return expression switch
        {
            SqlScalarRefExpression column => GetColumnName(column),
            SqlAggregateFunctionCallExpression aggregateFunction => aggregateFunction.FunctionName,
            _ => throw new TdsQueryEngineException("GROUP BY SELECT expression must have an alias."),
        };
    }

    private static string GetColumnName(SqlScalarRefExpression column)
    {
        return column is SqlColumnRefExpression columnRef ? columnRef.ColumnName.Value : column.MultipartIdentifier[column.MultipartIdentifier.Count - 1].Value;
    }

    private sealed class ParameterNameVisitor : ExpressionVisitor
    {
        private Dictionary<ParameterExpression, ParameterExpression> _scope = [];
        private readonly Dictionary<string, int> _nameCounters = new(StringComparer.OrdinalIgnoreCase);

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            var previousScope = _scope;
            _scope = new Dictionary<ParameterExpression, ParameterExpression>(previousScope);

            var renamedParameters = new ParameterExpression[node.Parameters.Count];
            for (var i = 0; i < node.Parameters.Count; i++)
            {
                var parameter = node.Parameters[i];
                var renamedParameter = CreateRenamedParameter(parameter.Type);
                _scope[parameter] = renamedParameter;
                renamedParameters[i] = renamedParameter;
            }

            var body = Visit(node.Body);
            _scope = previousScope;
            return Expression.Lambda(body, node.Name, node.TailCall, renamedParameters);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return _scope.TryGetValue(node, out var renamedParameter) ? renamedParameter : node;
        }

        private ParameterExpression CreateRenamedParameter(Type parameterType)
        {
            var baseName = GetTypeParameterName(parameterType);
            var nextIndex = _nameCounters.TryGetValue(baseName, out var currentIndex) ? currentIndex + 1 : 1;
            _nameCounters[baseName] = nextIndex;
            var parameterName = nextIndex == 1 ? baseName : baseName + nextIndex.ToString(CultureInfo.InvariantCulture);
            return Expression.Parameter(parameterType, parameterName);
        }
    }

    private sealed record QuerySource(IQueryable Query, Type RowType, IReadOnlyDictionary<string, AliasBinding> Aliases);

    private sealed record GroupedQuerySource(IQueryable Query, Type GroupType, Type SourceRowType, IReadOnlyDictionary<string, AliasBinding> Keys, IReadOnlyDictionary<string, AliasBinding> Aliases);

    private sealed record AliasBinding(Type Type, Func<Expression, Expression> Access, bool IsOuter = false, bool IsNullable = false);
}
