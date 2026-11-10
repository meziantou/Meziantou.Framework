using System.Linq.Expressions;
using System.Reflection;
using Meziantou.Framework.Tds.Handler;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;

namespace Meziantou.Framework.Tds.QueryEngine;

internal sealed partial class TdsQueryEngineExecutor
{
    /// <summary>Marks a <c>DEFAULT</c> value: the column keeps the value set by the constructor of the row.</summary>
    private static readonly object DefaultColumnValue = new();

    private Func<CancellationToken, ValueTask<long>> TranslateInsertStatement(SqlInsertStatement statement, IReadOnlyDictionary<string, TdsQueryParameter> parameters, QueryExecutionContext queryExecutionContext)
    {
        var cteRoots = BuildCteRoots(statement.QueryWithClause, GetMaxRecursion(statement), parameters, queryExecutionContext);
        var specification = statement.InsertSpecification;
        EnsureNoOutputClause(specification);
        if (specification.TopSpecification is not null)
        {
            throw new TdsQueryEngineException("TOP is not supported in INSERT statements.");
        }

        if (specification.Target is not SqlTableRefExpression target)
        {
            throw new TdsQueryEngineException("The target of an INSERT statement must be a query root.");
        }

        var tableName = target.ObjectIdentifier.ObjectName.Value;
        var (_, writer) = GetQueryRootWriter(tableName, cteRoots, queryExecutionContext, TdsQueryEngineResourceKind.QueryRootInsert, "INSERT");
        var insert = writer.Insert ?? throw CreateStatementNotSupportedError(tableName, "INSERT");
        var rowType = writer.RowType;
        if (!rowType.IsValueType && rowType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new TdsQueryEngineException($"Cannot insert rows into '{tableName}': its row type '{rowType.Name}' has no public parameterless constructor.");
        }

        var nullabilityContext = new NullabilityInfoContext();
        WritableColumn[] columns;
        if (specification.Source is SqlDefaultValuesInsertSource)
        {
            columns = [];
        }
        else if (specification.TargetColumns is { Count: > 0 })
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            columns = new WritableColumn[specification.TargetColumns.Count];
            for (var i = 0; i < columns.Length; i++)
            {
                var columnName = GetColumnName(specification.TargetColumns[i]);
                if (!names.Add(columnName))
                {
                    throw CreateDuplicateAssignmentError(columnName, "column list of an INSERT");
                }

                columns[i] = GetWritableColumn(rowType, columnName, nullabilityContext);
            }
        }
        else
        {
            // As SQL Server skips computed columns, the read-only members are not part of the implicit column list
            columns = [.. GetReadableMembers(rowType).Where(IsWritableMember).Select(member => CreateWritableColumn(member, nullabilityContext))];
            if (columns.Length == 0)
            {
                throw new TdsQueryEngineException($"The query root '{tableName}' has no column that can be inserted.");
            }
        }

        Func<CancellationToken, ValueTask<IReadOnlyList<object?[]>>> getValues = specification.Source switch
        {
            SqlDefaultValuesInsertSource => _ => ValueTask.FromResult<IReadOnlyList<object?[]>>([[]]),
            SqlTableConstructorInsertSource valuesSource => TranslateInsertValues(valuesSource.TableConstructorExpression, columns.Length, parameters, cteRoots, queryExecutionContext),
            SqlSelectSpecificationInsertSource selectSource => TranslateInsertSelect(selectSource.SelectSpecification, [.. columns.Select(column => column.Member.Name)], parameters, cteRoots, queryExecutionContext),
            _ => throw new TdsQueryEngineException("INSERT statements support VALUES, SELECT and DEFAULT VALUES."),
        };

        return async cancellationToken =>
        {
            var values = await getValues(cancellationToken).ConfigureAwait(false);
            var rows = new List<object>(values.Count);
            foreach (var rowValues in values)
            {
                var row = Activator.CreateInstance(rowType)!;
                for (var i = 0; i < columns.Length; i++)
                {
                    if (rowValues[i] != DefaultColumnValue)
                    {
                        columns[i].SetValue(row, ConvertColumnValue(columns[i], rowValues[i], tableName, "INSERT"));
                    }
                }

                rows.Add(row);
            }

            if (rows.Count > 0)
            {
                await insert(queryExecutionContext.QueryContext, rows, cancellationToken).ConfigureAwait(false);
            }

            return rows.Count;
        };
    }

    private Func<CancellationToken, ValueTask<IReadOnlyList<object?[]>>> TranslateInsertValues(SqlTableConstructorExpression tableConstructor, int columnCount, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, QueryExecutionContext queryExecutionContext)
    {
        // The values do not depend on a row, but they can call functions or scalar subqueries, so they are computed
        // when the statement runs
        var noRow = Expression.Parameter(typeof(object), "row");
        var noAliases = new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<Func<object?, object?>?[]>();
        foreach (var rowConstructor in tableConstructor.Children.OfType<SqlRowConstructorExpression>())
        {
            if (rowConstructor.Values.Count != columnCount)
            {
                throw rowConstructor.Values.Count > columnCount
                    ? new TdsQueryEngineException("There are fewer columns in the INSERT statement than values specified in the VALUES clause. The number of values in the VALUES clause must match the number of columns specified in the INSERT statement.", errorNumber: 110)
                    : new TdsQueryEngineException("There are more columns in the INSERT statement than values specified in the VALUES clause. The number of values in the VALUES clause must match the number of columns specified in the INSERT statement.", errorNumber: 109);
            }

            var row = new Func<object?, object?>?[columnCount];
            for (var i = 0; i < columnCount; i++)
            {
                if (rowConstructor.Values[i] is SqlLiteralExpression { Type: LiteralValueType.Default })
                {
                    continue;
                }

                var value = BuildScalar(rowConstructor.Values[i], noAliases, noRow, parameters, cteRoots, queryExecutionContext);
                row[i] = Expression.Lambda<Func<object?, object?>>(Expression.Convert(value, typeof(object)), noRow).Compile();
            }

            rows.Add(row);
        }

        return _ =>
        {
            var values = new List<object?[]>(rows.Count);
            foreach (var row in rows)
            {
                values.Add([.. row.Select(value => value is null ? DefaultColumnValue : value(null))]);
            }

            return ValueTask.FromResult<IReadOnlyList<object?[]>>(values);
        };
    }

    private Func<CancellationToken, ValueTask<IReadOnlyList<object?[]>>> TranslateInsertSelect(SqlSelectSpecification selectSpecification, string[] columnNames, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, QueryExecutionContext queryExecutionContext)
    {
        // The values are matched to the columns by position, so the selected expressions do not need names, as in SQL Server
        var namesByPosition = new PositionalColumnNames(columnNames, CreateCountMismatchError);
        var query = TranslateSelectSpecification(selectSpecification, parameters, cteRoots, queryExecutionContext, namesByPosition);
        var members = GetReadableMembers(query.ElementType);
        namesByPosition.EnsureCount(members.Count);

        return async cancellationToken =>
        {
            var rows = await _options.MaterializeAsync(query, cancellationToken).ConfigureAwait(false);
            var values = new List<object?[]>(rows.Count);
            foreach (var row in rows)
            {
                values.Add([.. members.Select(member => row is null ? null : GetMemberValue(member, row))]);
            }

            return values;
        };

        TdsQueryEngineException CreateCountMismatchError(int count)
        {
            return count < columnNames.Length
                ? new TdsQueryEngineException("The select list for the INSERT statement contains fewer items than the insert list. The number of SELECT values must match the number of INSERT columns.", errorNumber: 120)
                : new TdsQueryEngineException("The select list for the INSERT statement contains more items than the insert list. The number of SELECT values must match the number of INSERT columns.", errorNumber: 121);
        }
    }

    private Func<CancellationToken, ValueTask<long>> TranslateUpdateStatement(SqlUpdateStatement statement, IReadOnlyDictionary<string, TdsQueryParameter> parameters, QueryExecutionContext queryExecutionContext)
    {
        var cteRoots = BuildCteRoots(statement.QueryWithClause, GetMaxRecursion(statement), parameters, queryExecutionContext);
        var specification = statement.UpdateSpecification;
        var target = TranslateUpdateDeleteTarget(specification, parameters, cteRoots, queryExecutionContext, TdsQueryEngineResourceKind.QueryRootUpdate, "UPDATE");
        var update = target.Writer.Update ?? throw CreateStatementNotSupportedError(target.TableName, "UPDATE");

        // Every SET expression reads the values of the row before the statement
        var nullabilityContext = new NullabilityInfoContext();
        var rowParameter = Expression.Parameter(target.Source.RowType, "row");
        var columns = new List<WritableColumn>();
        var values = new List<Expression>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in specification.SetClause.Assignments)
        {
            if (assignment is not SqlColumnAssignment columnAssignment)
            {
                throw new TdsQueryEngineException("Only column assignments are supported in the SET clause of an UPDATE statement.");
            }

            var qualifier = GetQualifier(columnAssignment.Column);
            if (qualifier is not null && !target.Source.Aliases.ContainsKey(qualifier))
            {
                throw new TdsQueryEngineException($"Unknown table alias '{qualifier}'.");
            }

            var columnName = GetColumnName(columnAssignment.Column);
            if (!names.Add(columnName))
            {
                throw CreateDuplicateAssignmentError(columnName, "SET clause of an UPDATE");
            }

            columns.Add(GetWritableColumn(target.Source.RowType, columnName, nullabilityContext));
            var valueExpression = columnAssignment.Operator switch
            {
                SqlAssignmentOperatorType.Assignment => columnAssignment.Value,
                _ => ParseScalarExpression($"{columnAssignment.Column.Sql} {GetCompoundAssignmentOperator(columnAssignment.Operator)} ({columnAssignment.Value.Sql})"),
            };
            var value = BuildScalar(valueExpression, target.Source.Aliases, rowParameter, parameters, cteRoots, queryExecutionContext);
            values.Add(Expression.Convert(value, typeof(object)));
        }

        var objectParameter = Expression.Parameter(typeof(object), "value");
        var computeValues = Expression.Lambda<Func<object, object?[]>>(
            Expression.Block(
                [rowParameter],
                Expression.Assign(rowParameter, Expression.Convert(objectParameter, target.Source.RowType)),
                Expression.NewArrayInit(typeof(object), values)),
            objectParameter).Compile();

        return async cancellationToken =>
        {
            var rows = await MaterializeRowsAsync(target.Query, cancellationToken).ConfigureAwait(false);

            // The new values are computed and converted for every row before the first row changes, so an invalid
            // value leaves all the rows unchanged
            var newValues = new List<object?[]>(rows.Count);
            foreach (var row in rows)
            {
                var rowValues = computeValues(row);
                for (var i = 0; i < rowValues.Length; i++)
                {
                    rowValues[i] = ConvertColumnValue(columns[i], rowValues[i], target.TableName, "UPDATE");
                }

                newValues.Add(rowValues);
            }

            for (var i = 0; i < rows.Count; i++)
            {
                for (var j = 0; j < columns.Count; j++)
                {
                    columns[j].SetValue(rows[i], newValues[i][j]);
                }
            }

            if (rows.Count > 0)
            {
                await update(queryExecutionContext.QueryContext, rows, cancellationToken).ConfigureAwait(false);
            }

            return rows.Count;
        };
    }

    private Func<CancellationToken, ValueTask<long>> TranslateDeleteStatement(SqlDeleteStatement statement, IReadOnlyDictionary<string, TdsQueryParameter> parameters, QueryExecutionContext queryExecutionContext)
    {
        var cteRoots = BuildCteRoots(statement.QueryWithClause, GetMaxRecursion(statement), parameters, queryExecutionContext);
        var target = TranslateUpdateDeleteTarget(statement.DeleteSpecification, parameters, cteRoots, queryExecutionContext, TdsQueryEngineResourceKind.QueryRootDelete, "DELETE");
        var delete = target.Writer.Delete ?? throw CreateStatementNotSupportedError(target.TableName, "DELETE");

        return async cancellationToken =>
        {
            var rows = await MaterializeRowsAsync(target.Query, cancellationToken).ConfigureAwait(false);
            if (rows.Count > 0)
            {
                await delete(queryExecutionContext.QueryContext, rows, cancellationToken).ConfigureAwait(false);
            }

            return rows.Count;
        };
    }

    /// <summary>Translates the target, FROM, WHERE and TOP clauses of an UPDATE or DELETE statement to the query of the rows to modify.</summary>
    private UpdateDeleteTarget TranslateUpdateDeleteTarget(SqlUpdateDeleteSpecificationBase specification, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, QueryExecutionContext queryExecutionContext, TdsQueryEngineResourceKind resourceKind, string statementName)
    {
        EnsureNoOutputClause(specification);
        if (specification.Target is not SqlTableRefExpression target)
        {
            throw new TdsQueryEngineException($"The target of an {statementName} statement must be a query root.");
        }

        // UPDATE alias SET ... FROM table AS alias: the FROM clause names the query root of the target
        var table = target;
        if (specification.FromClause is not null)
        {
            if (specification.FromClause.TableExpressions.Count != 1 ||
                specification.FromClause.TableExpressions[0] is not SqlTableRefExpression fromTable ||
                !string.Equals(fromTable.Alias?.Value ?? fromTable.ObjectIdentifier.ObjectName.Value, target.ObjectIdentifier.ObjectName.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new TdsQueryEngineException($"The FROM clause of an {statementName} statement must contain only the target of the statement.");
            }

            table = fromTable;
        }

        var tableName = table.ObjectIdentifier.ObjectName.Value;
        var (queryRoot, writer) = GetQueryRootWriter(tableName, cteRoots, queryExecutionContext, resourceKind, statementName);
        var query = queryExecutionContext.QueryContext.ResolveQuery(queryRoot);
        if (!writer.RowType.IsAssignableFrom(query.ElementType))
        {
            throw new TdsQueryEngineException($"The rows of the query root '{tableName}' are not of type '{writer.RowType.Name}'.");
        }

        var source = new QuerySource(
            query,
            query.ElementType,
            new Dictionary<string, AliasBinding>(StringComparer.OrdinalIgnoreCase)
            {
                [table.Alias?.Value ?? tableName] = new AliasBinding(query.ElementType, expression => expression),
            });
        if (specification.WhereClause?.Expression is not null)
        {
            source = ApplyWhere(source, specification.WhereClause.Expression, parameters, cteRoots, queryExecutionContext);
        }

        var rowsQuery = source.Query;
        if (specification.TopSpecification is not null)
        {
            rowsQuery = ApplyTop(rowsQuery, specification.TopSpecification, parameters);
        }

        return new UpdateDeleteTarget(tableName, writer, source, RenameQueryParameters(rowsQuery));
    }

    private (TdsQueryRoot QueryRoot, TdsQueryRootWriter Writer) GetQueryRootWriter(string tableName, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, QueryExecutionContext queryExecutionContext, TdsQueryEngineResourceKind resourceKind, string statementName)
    {
        if (cteRoots.ContainsKey(tableName))
        {
            throw new TdsQueryEngineException($"The common table expression '{tableName}' cannot be the target of an {statementName} statement.");
        }

        if (!queryExecutionContext.QueryRoots.TryGetValue(tableName, out var queryRoot))
        {
            throw new TdsQueryEngineException($"Unknown query root '{tableName}'.");
        }

        EnsureAuthorized(queryExecutionContext.QueryContext, resourceKind, tableName);
        return (queryRoot, queryRoot.Writer ?? throw CreateStatementNotSupportedError(tableName, statementName));
    }

    private async ValueTask<List<object>> MaterializeRowsAsync(IQueryable query, CancellationToken cancellationToken)
    {
        var rows = await _options.MaterializeAsync(query, cancellationToken).ConfigureAwait(false);
        return [.. rows.OfType<object>()];
    }

    private static void EnsureNoOutputClause(SqlDmlSpecification specification)
    {
        if (specification.OutputClause is not null || specification.OutputIntoClause is not null)
        {
            throw new TdsQueryEngineException("The OUTPUT clause is not supported.");
        }
    }

    private static TdsQueryEngineException CreateStatementNotSupportedError(string tableName, string statementName)
    {
        return new TdsQueryEngineException($"The query root '{tableName}' does not support {statementName} statements.");
    }

    private static TdsQueryEngineException CreateDuplicateAssignmentError(string columnName, string clause)
    {
        return new TdsQueryEngineException($"The column name '{columnName}' is specified more than once in the {clause}. A column cannot be assigned more than one value in the same clause.", errorNumber: 264);
    }

    private static string GetCompoundAssignmentOperator(SqlAssignmentOperatorType operatorType)
    {
        return operatorType switch
        {
            SqlAssignmentOperatorType.AdditionAssignment => "+",
            SqlAssignmentOperatorType.SubtractionAssignment => "-",
            SqlAssignmentOperatorType.MultiplicationAssignment => "*",
            SqlAssignmentOperatorType.DivisionAssignment => "/",
            SqlAssignmentOperatorType.ModulusAssignment => "%",
            SqlAssignmentOperatorType.BitwiseAndAssignment => "&",
            SqlAssignmentOperatorType.BitwiseOrAssignment => "|",
            SqlAssignmentOperatorType.BitwiseXorAssignment => "^",
            SqlAssignmentOperatorType.ConcatAssignment => "||",
            _ => throw new TdsQueryEngineException($"The assignment operator '{operatorType}' is not supported."),
        };
    }

    private static WritableColumn GetWritableColumn(Type rowType, string columnName, NullabilityInfoContext nullabilityContext)
    {
        var member = FindMember(rowType, columnName) ?? throw new TdsQueryEngineException($"Invalid column name '{columnName}'.", errorNumber: 207);
        if (!IsWritableMember(member))
        {
            throw new TdsQueryEngineException($"The column '{member.Name}' cannot be modified because it is read-only.", errorNumber: 271);
        }

        return CreateWritableColumn(member, nullabilityContext);
    }

    private static bool IsWritableMember(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo property => property.SetMethod is { IsPublic: true },
            FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
            _ => false,
        };
    }

    private static WritableColumn CreateWritableColumn(MemberInfo member, NullabilityInfoContext nullabilityContext)
    {
        var type = GetMemberType(member);

        // A non-nullable reference type is a NOT NULL column, as a non-nullable value type
        var allowsNull = type.IsValueType
            ? IsNullableValueType(type)
            : member switch
            {
                PropertyInfo property => nullabilityContext.Create(property).WriteState is not NullabilityState.NotNull,
                FieldInfo field => nullabilityContext.Create(field).WriteState is not NullabilityState.NotNull,
                _ => true,
            };
        return new WritableColumn(member, type, allowsNull);
    }

    private static object? ConvertColumnValue(WritableColumn column, object? value, string tableName, string statementName)
    {
        if (value is null or DBNull)
        {
            if (!column.AllowsNull)
            {
                throw new TdsQueryEngineException($"Cannot insert the value NULL into column '{column.Member.Name}', table '{tableName}'; column does not allow nulls. {statementName} fails.", errorNumber: 515);
            }

            return null;
        }

        return ConvertValue(value, column.Type);
    }

    private static object? GetMemberValue(MemberInfo member, object instance)
    {
        return member switch
        {
            PropertyInfo property => property.GetValue(instance),
            FieldInfo field => field.GetValue(instance),
            _ => throw new TdsQueryEngineException($"Unsupported member '{member.Name}'."),
        };
    }

    private sealed record WritableColumn(MemberInfo Member, Type Type, bool AllowsNull)
    {
        public void SetValue(object row, object? value)
        {
            switch (Member)
            {
                case PropertyInfo property:
                    property.SetValue(row, value);
                    break;

                case FieldInfo field:
                    field.SetValue(row, value);
                    break;
            }
        }
    }

    private sealed record UpdateDeleteTarget(string TableName, TdsQueryRootWriter Writer, QuerySource Source, IQueryable Query);
}
