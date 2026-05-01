using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Meziantou.Framework.Tds.Handler;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using SqlParser = Microsoft.SqlServer.Management.SqlParser.Parser.Parser;
using SqlParserParseOptions = Microsoft.SqlServer.Management.SqlParser.Parser.ParseOptions;

namespace Meziantou.Framework.Tds.QueryEngine;

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

        if (selectStatement.SelectSpecification.QueryExpression is SqlQuerySpecification querySpecification)
        {
            return TranslateQuerySpecification(querySpecification, selectStatement.SelectSpecification.OrderByClause, parameters);
        }

        var query = TranslateQueryExpression(selectStatement.SelectSpecification.QueryExpression, parameters);
        if (selectStatement.SelectSpecification.OrderByClause is not null)
        {
            query = ApplyOrderBy(CreateProjectionSource(query), selectStatement.SelectSpecification.OrderByClause, parameters).Query;
        }

        return query;
    }

    private IQueryable TranslateQueryExpression(SqlQueryExpression queryExpression, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        return queryExpression switch
        {
            SqlQuerySpecification querySpecification => TranslateQuerySpecification(querySpecification, orderByOverride: null, parameters),
            SqlBinaryQueryExpression binaryQueryExpression => TranslateBinaryQueryExpression(binaryQueryExpression, parameters),
            _ => throw new TdsQueryEngineException("Only query specifications and UNION query expressions are supported."),
        };
    }

    private IQueryable TranslateBinaryQueryExpression(SqlBinaryQueryExpression queryExpression, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var left = TranslateQueryExpression(queryExpression.Left, parameters);
        var right = TranslateQueryExpression(queryExpression.Right, parameters);
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

    private IQueryable TranslateQuerySpecification(SqlQuerySpecification querySpecification, SqlOrderByClause? orderByOverride, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        if (querySpecification.IntoClause is not null ||
            querySpecification.WindowClause is not null ||
            querySpecification.ForClause is not null)
        {
            throw new TdsQueryEngineException("The SQL query uses a SELECT feature that is not supported.");
        }

        var source = BuildSource(querySpecification.FromClause);
        if (querySpecification.WhereClause?.Expression is not null)
        {
            source = ApplyWhere(source, querySpecification.WhereClause.Expression, parameters);
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

    private QuerySource BuildSource(SqlFromClause? fromClause)
    {
        if (fromClause is null || fromClause.TableExpressions.Count != 1)
        {
            throw new TdsQueryEngineException("The SQL query must contain exactly one FROM table expression.");
        }

        return BuildSource(fromClause.TableExpressions[0]);
    }

    private QuerySource BuildSource(SqlTableExpression tableExpression)
    {
        if (tableExpression is SqlTableRefExpression tableRefExpression)
        {
            return BuildTableSource(tableRefExpression);
        }

        if (tableExpression is SqlQualifiedJoinTableExpression joinExpression)
        {
            return BuildJoinSource(joinExpression);
        }

        throw new TdsQueryEngineException("Only table references and INNER JOIN table expressions are supported.");
    }

    private QuerySource BuildTableSource(SqlTableRefExpression tableExpression)
    {
        var tableName = tableExpression.ObjectIdentifier.ObjectName.Value;
        var root = _options.QueryRoots.FirstOrDefault(candidate => string.Equals(candidate.Name, tableName, StringComparison.OrdinalIgnoreCase));
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

    private QuerySource BuildJoinSource(SqlQualifiedJoinTableExpression joinExpression)
    {
        if (joinExpression.JoinOperator != SqlJoinOperatorType.InnerJoin)
        {
            throw new TdsQueryEngineException("Only INNER JOIN is supported.");
        }

        var left = BuildSource(joinExpression.Left);
        if (joinExpression.Right is not SqlTableRefExpression rightTable)
        {
            throw new TdsQueryEngineException("Only table references are supported on the right side of INNER JOIN.");
        }

        var right = BuildTableSource(rightTable);
        if (joinExpression.OnClause?.Expression is not SqlComparisonBooleanExpression { ComparisonOperator: SqlComparisonBooleanExpressionType.Equals } comparison)
        {
            throw new TdsQueryEngineException("INNER JOIN requires a simple equality ON clause.");
        }

        if (!ReferencesAliases(comparison.Left, left.Aliases) && !ReferencesAliases(comparison.Right, left.Aliases))
        {
            throw new TdsQueryEngineException("INNER JOIN must compare a column from the left source.");
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
        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Join),
            [left.RowType, right.RowType, leftKey.Type, carrierType],
            left.Query.Expression,
            right.Query.Expression,
            Expression.Quote(Expression.Lambda(leftKey, leftParameter)),
            Expression.Quote(Expression.Lambda(rightKey, rightParameter)),
            Expression.Quote(Expression.Lambda(resultBody, leftParameter, rightParameter)));

        var aliases = new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in carrierMembers)
        {
            aliases.Add(member.Name, new AliasBinding(member.Type, expression => Expression.Property(expression, member.Name)));
        }

        return new QuerySource(left.Query.Provider.CreateQuery(call), carrierType, aliases);
    }

    private QuerySource ApplyWhere(QuerySource source, SqlBooleanExpression expression, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var parameter = Expression.Parameter(source.RowType, "row");
        var predicate = BuildBoolean(expression, source.Aliases, parameter, parameters);
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

    private static GroupedQuerySource ApplyGroupBy(QuerySource source, SqlGroupByClause groupByClause)
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

    private static GroupedQuerySource ApplyHaving(GroupedQuerySource source, SqlBooleanExpression expression, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
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

    private static IQueryable ApplyGroupSelect(GroupedQuerySource source, SqlSelectClause selectClause)
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

    private Expression BuildBoolean(SqlBooleanExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        return expression switch
        {
            SqlComparisonBooleanExpression comparison => BuildComparison(comparison, aliases, parameter, parameters),
            SqlInBooleanExpression inExpression => BuildInBoolean(inExpression, aliases, parameter, parameters),
            SqlIsNullBooleanExpression isNullExpression => BuildIsNullBoolean(isNullExpression, aliases, parameter, parameters),
            SqlBinaryBooleanExpression { Operator: SqlBooleanOperatorType.And } binary => Expression.AndAlso(
                BuildBoolean(binary.Left, aliases, parameter, parameters),
                BuildBoolean(binary.Right, aliases, parameter, parameters)),
            SqlBinaryBooleanExpression { Operator: SqlBooleanOperatorType.Or } orBinary => Expression.OrElse(
                BuildBoolean(orBinary.Left, aliases, parameter, parameters),
                BuildBoolean(orBinary.Right, aliases, parameter, parameters)),
            _ => throw new TdsQueryEngineException("Only comparison, IN, and IS NULL predicates combined with AND/OR are supported."),
        };
    }

    private static BinaryExpression BuildComparison(SqlComparisonBooleanExpression comparison, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
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

    private Expression BuildInBoolean(SqlInBooleanExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var inExpression = BuildScalar(expression.InExpression, aliases, parameter, parameters);
        Expression containsExpression = expression.ComparisonValue switch
        {
            SqlInBooleanExpressionCollectionValue collectionValue => BuildInCollectionExpression(collectionValue, inExpression, aliases, parameter, parameters),
            SqlInBooleanExpressionQueryValue queryValue => BuildInSubqueryExpression(queryValue, inExpression, parameters),
            _ => throw new TdsQueryEngineException("Only IN collections and simple IN subqueries are supported."),
        };

        return expression.HasNot ? Expression.Not(containsExpression) : containsExpression;
    }

    private static MethodCallExpression BuildInCollectionExpression(SqlInBooleanExpressionCollectionValue collectionValue, Expression inExpression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
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

    private MethodCallExpression BuildInSubqueryExpression(SqlInBooleanExpressionQueryValue queryValue, Expression inExpression, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
    {
        var subquery = TranslateInSubquery(queryValue.Value, inExpression.Type, parameters);
        return Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Contains),
            [inExpression.Type],
            subquery.Expression,
            inExpression);
    }

    private static Expression BuildIsNullBoolean(SqlIsNullBooleanExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
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

    private IQueryable TranslateInSubquery(SqlQueryExpression queryExpression, Type targetType, IReadOnlyDictionary<string, TdsQueryParameter> parameters)
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

        var source = BuildSource(querySpecification.FromClause);
        if (querySpecification.WhereClause?.Expression is not null)
        {
            source = ApplyWhere(source, querySpecification.WhereClause.Expression, parameters);
        }

        var parameter = Expression.Parameter(source.RowType, "row");
        var value = BuildScalar(selectExpression.Expression, source.Aliases, parameter, parameters, targetType);
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

    private static Expression BuildGroupBoolean(SqlBooleanExpression expression, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
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

    private static Expression BuildGroupIsNullBoolean(SqlIsNullBooleanExpression expression, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
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

    private static BinaryExpression BuildGroupComparison(SqlComparisonBooleanExpression comparison, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters)
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

    private static Expression BuildGroupScalar(SqlScalarExpression expression, GroupedQuerySource source, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Type? targetType = null)
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
                return Expression.Constant(ConvertLiteral(literal, targetType), targetType ?? GetLiteralType(literal));

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

    private static MethodCallExpression BuildAggregateFunction(SqlAggregateFunctionCallExpression aggregateFunction, GroupedQuerySource source, ParameterExpression parameter)
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

    private static MethodCallExpression BuildAggregateFunctionWithSelector(SqlAggregateFunctionCallExpression aggregateFunction, GroupedQuerySource source, ParameterExpression parameter, string methodName)
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

    private static Expression BuildScalar(SqlScalarExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, Type? targetType = null)
    {
        if (TryBuildScalar(expression, aliases, parameter, parameters, out var result, targetType))
        {
            return result;
        }

        throw new TdsQueryEngineException("Only column references, literals, and SQL parameters are supported in expressions.");
    }

    private static bool TryBuildScalar(SqlScalarExpression expression, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter, IReadOnlyDictionary<string, TdsQueryParameter>? parameters, out Expression result, Type? targetType = null)
    {
        switch (expression)
        {
            case SqlScalarRefExpression column:
                result = BuildColumn(column, aliases, parameter);
                return true;

            case SqlLiteralExpression literal:
                result = Expression.Constant(ConvertLiteral(literal, targetType), targetType ?? GetLiteralType(literal));
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

    private static MemberExpression BuildColumn(SqlScalarRefExpression column, IReadOnlyDictionary<string, AliasBinding> aliases, ParameterExpression parameter)
    {
        var qualifier = GetQualifier(column);
        var columnName = GetColumnName(column);
        AliasBinding? binding;
        if (qualifier is not null)
        {
            if (!aliases.TryGetValue(qualifier, out binding))
            {
                throw new TdsQueryEngineException($"Unknown table alias '{qualifier}'.");
            }
        }
        else if (aliases.Count == 1)
        {
            binding = aliases.Values.Single();
        }
        else
        {
            binding = aliases.Values.SingleOrDefault(candidate => FindMember(candidate.Type, columnName) is not null);
            if (binding is null)
            {
                throw new TdsQueryEngineException($"Ambiguous or unknown column '{columnName}'.");
            }
        }

        var member = FindMember(binding.Type, columnName);
        if (member is null)
        {
            throw new TdsQueryEngineException($"Unknown column '{columnName}'.");
        }

        return member switch
        {
            PropertyInfo property => Expression.Property(binding.Access(parameter), property),
            FieldInfo field => Expression.Field(binding.Access(parameter), field),
            _ => throw new TdsQueryEngineException($"Unsupported member '{member.Name}'."),
        };
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(property => property.GetIndexParameters().Length == 0 && property.CanRead && string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? (MemberInfo?)type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
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

    private static object? ConvertLiteral(SqlLiteralExpression literal, Type? targetType)
    {
        object? value = literal.Type switch
        {
            LiteralValueType.Null => null,
            LiteralValueType.Integer => long.Parse(literal.Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            LiteralValueType.Numeric or LiteralValueType.Money => decimal.Parse(literal.Value, NumberStyles.Number, CultureInfo.InvariantCulture),
            LiteralValueType.Real => double.Parse(literal.Value, NumberStyles.Float, CultureInfo.InvariantCulture),
            LiteralValueType.String or LiteralValueType.UnicodeString or LiteralValueType.Identifier => literal.Value,
            _ => throw new TdsQueryEngineException($"Literal type '{literal.Type}' is not supported."),
        };

        return targetType is null ? value : ConvertValue(value, targetType);
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

    private static Type GetLiteralType(SqlLiteralExpression literal)
    {
        return literal.Type switch
        {
            LiteralValueType.Null => typeof(object),
            LiteralValueType.Integer => typeof(long),
            LiteralValueType.Numeric or LiteralValueType.Money => typeof(decimal),
            LiteralValueType.Real => typeof(double),
            LiteralValueType.String or LiteralValueType.UnicodeString or LiteralValueType.Identifier => typeof(string),
            _ => typeof(object),
        };
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

    private sealed record QuerySource(IQueryable Query, Type RowType, IReadOnlyDictionary<string, AliasBinding> Aliases);

    private sealed record GroupedQuerySource(IQueryable Query, Type GroupType, Type SourceRowType, IReadOnlyDictionary<string, AliasBinding> Keys, IReadOnlyDictionary<string, AliasBinding> Aliases);

    private sealed record AliasBinding(Type Type, Func<Expression, Expression> Access);
}
