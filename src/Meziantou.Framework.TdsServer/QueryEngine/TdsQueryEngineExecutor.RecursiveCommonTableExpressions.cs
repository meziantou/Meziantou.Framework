using System.Collections;
using System.Linq.Expressions;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

internal sealed partial class TdsQueryEngineExecutor
{
    private const string UnsupportedStatementMessage = "Only SELECT, INSERT, UPDATE and DELETE statements are supported.";

    /// <summary>The recursion levels SQL Server allows when the statement has no MAXRECURSION hint.</summary>
    private const int DefaultMaxRecursion = 100;

    private const int MaxRecursionLimit = 32767;

    private static TdsQueryEngineException CreateColumnCountMismatchError()
    {
        return new TdsQueryEngineException("All queries combined using a UNION operator must have an equal number of expressions in their target lists.", errorNumber: 205);
    }

    /// <summary>Reads the <c>OPTION (MAXRECURSION n)</c> hint of a statement. 0 means no limit.</summary>
    private static int GetMaxRecursion(SqlStatement statement)
    {
        // The parser does not build nodes for the query hints: they are only in the tokens of the statement
        var tokens = statement.Tokens.Select(token => token.Text).Where(text => !IsTrivia(text)).ToArray();
        var maxRecursion = DefaultMaxRecursion;
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (!string.Equals(tokens[i], "OPTION", StringComparison.OrdinalIgnoreCase) || tokens[i + 1] is not "(")
            {
                continue;
            }

            for (var j = i + 2; j + 1 < tokens.Length && tokens[j] is not ")"; j++)
            {
                if (!string.Equals(tokens[j], "MAXRECURSION", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!int.TryParse(tokens[j + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > MaxRecursionLimit)
                {
                    throw new TdsQueryEngineException($"The value {tokens[j + 1]} specified for the MAXRECURSION option exceeds the allowed maximum of {MaxRecursionLimit}.", errorNumber: 310);
                }

                maxRecursion = value;
            }
        }

        return maxRecursion;

        static bool IsTrivia(string text) => string.IsNullOrWhiteSpace(text) || text.StartsWith("--", StringComparison.Ordinal) || text.StartsWith("/*", StringComparison.Ordinal);
    }

    private static bool IsRecursiveCommonTableExpression(SqlCommonTableExpression cte)
    {
        return CountReferences(cte.QueryExpression, cte.Name.Value) > 0;
    }

    /// <summary>Counts the table references to <paramref name="tableName"/> in <paramref name="node"/>, subqueries included.</summary>
    private static int CountReferences(SqlCodeObject node, string tableName)
    {
        // The tree comes from client SQL, so it is walked without recursion
        var count = 0;
        var stack = new Stack<SqlCodeObject>();
        stack.Push(node);
        while (stack.TryPop(out var current))
        {
            if (current is SqlTableRefExpression tableRef && IsTableReference(tableRef, tableName))
            {
                count++;
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return count;
    }

    /// <summary>Tells whether a table reference reads <paramref name="tableName"/>, matched as <see cref="BuildTableSource"/> matches common table expressions.</summary>
    private static bool IsTableReference(SqlTableRefExpression tableRef, string tableName)
    {
        return string.Equals(tableRef.ObjectIdentifier.ObjectName.Value, tableName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Translates <c>anchor UNION ALL recursive</c>. The anchor members run once, then the recursive members run on the
    /// rows of the previous level until they return no row, as SQL Server does.
    /// </summary>
    private IQueryable TranslateRecursiveCommonTableExpression(SqlCommonTableExpression cte, int maxRecursion, IReadOnlyDictionary<string, TdsQueryParameter> parameters, IReadOnlyDictionary<string, TdsQueryRoot> cteRoots, QueryExecutionContext queryExecutionContext)
    {
        var cteName = cte.Name.Value;
        var members = new List<(SqlBinaryQueryOperatorType? Operator, SqlQueryExpression Query)>();
        FlattenUnion(cte.QueryExpression, operatorType: null, members);

        var firstRecursiveMember = members.FindIndex(member => CountReferences(member.Query, cteName) > 0);
        if (firstRecursiveMember == 0)
        {
            throw new TdsQueryEngineException($"No anchor member was specified for recursive query '{cteName}'.", errorNumber: 246);
        }

        var anchorQuery = TranslateQueryExpression(members[0].Query, parameters, cteRoots, queryExecutionContext);
        for (var i = 1; i < firstRecursiveMember; i++)
        {
            var anchorMember = TranslateQueryExpression(members[i].Query, parameters, cteRoots, queryExecutionContext);
            anchorQuery = CombineUnion(anchorQuery, anchorMember, members[i].Operator!.Value);
        }

        if (cte.ColumnList is not null && cte.ColumnList.Count > 0)
        {
            anchorQuery = ApplyColumnList(anchorQuery, cte.ColumnList, scopeName: "CTE");
        }

        var recursiveMembers = new List<SqlQuerySpecification>();
        for (var i = firstRecursiveMember; i < members.Count; i++)
        {
            if (members[i].Operator is not SqlBinaryQueryOperatorType.UnionAll)
            {
                throw new TdsQueryEngineException($"Recursive common table expression '{cteName}' does not contain a top-level UNION ALL operator.", errorNumber: 252);
            }

            if (CountReferences(members[i].Query, cteName) == 0)
            {
                throw new TdsQueryEngineException($"The anchor members of recursive query '{cteName}' must come before its recursive members.");
            }

            if (members[i].Query is not SqlQuerySpecification recursiveMember)
            {
                throw new TdsQueryEngineException($"The recursive members of recursive query '{cteName}' must be SELECT statements combined with UNION ALL.");
            }

            ValidateRecursiveMember(recursiveMember, cteName);
            recursiveMembers.Add(recursiveMember);
        }

        // The anchor names the columns. A column is nullable when the anchor or a recursive member returns a nullable
        // value, so the rows of every level fit in the same type.
        var anchorColumns = GetReadableMembers(anchorQuery.ElementType);
        var columnNames = anchorColumns.Select(member => member.Name).ToArray();
        var columnTypes = anchorColumns.Select(GetMemberType).ToArray();
        var (workingTable, recursiveQueries) = TranslateRecursiveMembers(anchorQuery.ElementType);
        foreach (var recursiveQuery in recursiveQueries)
        {
            var recursiveColumns = GetReadableMembers(recursiveQuery.ElementType);
            if (recursiveColumns.Count != columnTypes.Length)
            {
                throw CreateColumnCountMismatchError();
            }

            for (var i = 0; i < columnTypes.Length; i++)
            {
                var recursiveType = GetMemberType(recursiveColumns[i]);
                if (GetNonNullableType(recursiveType) != GetNonNullableType(columnTypes[i]))
                {
                    throw new TdsQueryEngineException($"Types don't match between the anchor and the recursive part in column '{columnNames[i]}' of recursive query '{cteName}'.", errorNumber: 240);
                }

                if (IsNullableValueType(recursiveType))
                {
                    columnTypes[i] = MakeNullableType(columnTypes[i]);
                }
            }
        }

        var cteType = TdsProjectionTypeFactory.GetProjectionType([.. columnNames.Zip(columnTypes, (name, type) => new TdsProjectionMember(name, type))]);
        if (anchorQuery.ElementType != cteType)
        {
            // The recursive members read the rows of the previous level, so they are translated again with the final type
            anchorQuery = ConvertRows(anchorQuery, cteType);
            (workingTable, recursiveQueries) = TranslateRecursiveMembers(cteType);
        }

        var convertedRecursiveQueries = recursiveQueries.Select(query => query.ElementType == cteType ? query : ConvertRows(query, cteType)).ToArray();
        var result = (IEnumerable)Activator.CreateInstance(
            typeof(RecursiveCommonTableExpression<>).MakeGenericType(cteType),
            cteName,
            anchorQuery,
            convertedRecursiveQueries,
            workingTable,
            maxRecursion,
            _options.MaxRecursiveCommonTableExpressionRows,
            queryExecutionContext.CancellationToken)!;
        return Queryable.AsQueryable(result);

        (IRecursiveCommonTableExpressionWorkingTable WorkingTable, IQueryable[] Queries) TranslateRecursiveMembers(Type rowType)
        {
            var table = (IRecursiveCommonTableExpressionWorkingTable)Activator.CreateInstance(typeof(RecursiveCommonTableExpressionWorkingTable<>).MakeGenericType(rowType), cteName)!;
            var recursiveCteRoots = new Dictionary<string, TdsQueryRoot>(cteRoots, StringComparer.OrdinalIgnoreCase)
            {
                [cteName] = new TdsQueryRoot(cteName, Queryable.AsQueryable(table)),
            };

            var queries = recursiveMembers
                .Select(member => TranslateQuerySpecification(member, orderByOverride: null, parameters, recursiveCteRoots, queryExecutionContext, columnNames))
                .ToArray();
            return (table, queries);
        }
    }

    private static void FlattenUnion(SqlQueryExpression queryExpression, SqlBinaryQueryOperatorType? operatorType, List<(SqlBinaryQueryOperatorType? Operator, SqlQueryExpression Query)> members)
    {
        // UNION and UNION ALL are left-associative, so the left operand is the deepest and the walk ends on the leftmost member
        var rightOperands = new Stack<(SqlBinaryQueryOperatorType? Operator, SqlQueryExpression Query)>();
        rightOperands.Push((operatorType, queryExpression));
        while (rightOperands.TryPop(out var item))
        {
            if (item.Query is SqlBinaryQueryExpression { Operator: SqlBinaryQueryOperatorType.Union or SqlBinaryQueryOperatorType.UnionAll } binary)
            {
                rightOperands.Push((binary.Operator, binary.Right));
                rightOperands.Push((item.Operator, binary.Left));
            }
            else
            {
                members.Add(item);
            }
        }
    }

    private static IQueryable CombineUnion(IQueryable left, IQueryable right, SqlBinaryQueryOperatorType operatorType)
    {
        if (left.ElementType != right.ElementType)
        {
            throw new TdsQueryEngineException("UNION query expressions must project the same columns and types.");
        }

        var call = Expression.Call(
            typeof(Queryable),
            operatorType is SqlBinaryQueryOperatorType.UnionAll ? nameof(Queryable.Concat) : nameof(Queryable.Union),
            [left.ElementType],
            left.Expression,
            right.Expression);
        return left.Provider.CreateQuery(call);
    }

    private static void ValidateRecursiveMember(SqlQuerySpecification member, string cteName)
    {
        // The numbers and messages are the ones of SQL Server
        if (member.SelectClause.IsDistinct)
        {
            throw new TdsQueryEngineException($"DISTINCT operator is not allowed in the recursive part of a recursive common table expression '{cteName}'.", errorNumber: 460);
        }

        if (member.SelectClause.Top is not null)
        {
            throw new TdsQueryEngineException($"TOP operator is not allowed in the recursive part of a recursive common table expression '{cteName}'.", errorNumber: 461);
        }

        if (member.GroupByClause is not null || member.HavingClause is not null || ContainsAggregate(member.SelectClause))
        {
            throw new TdsQueryEngineException($"GROUP BY, HAVING, or aggregate functions are not allowed in the recursive part of a recursive common table expression '{cteName}'.", errorNumber: 467);
        }

        if (member.OrderByClause is not null)
        {
            throw new TdsQueryEngineException("The ORDER BY clause is invalid in views, inline functions, derived tables, subqueries, and common table expressions, unless TOP, OFFSET or FOR XML is also specified.", errorNumber: 1033);
        }

        var hasOuterJoin = false;
        var directReferences = 0;
        var tableExpressions = new Stack<SqlTableExpression>((IEnumerable<SqlTableExpression>?)member.FromClause?.TableExpressions ?? []);
        while (tableExpressions.TryPop(out var tableExpression))
        {
            switch (tableExpression)
            {
                case SqlTableRefExpression tableRef when IsTableReference(tableRef, cteName):
                    directReferences++;
                    break;

                case SqlQualifiedJoinTableExpression join:
                    hasOuterJoin |= join.JoinOperator is not SqlJoinOperatorType.InnerJoin;
                    tableExpressions.Push(join.Left);
                    tableExpressions.Push(join.Right);
                    break;

                case SqlUnqualifiedJoinTableExpression join:
                    tableExpressions.Push(join.Left);
                    tableExpressions.Push(join.Right);
                    break;
            }
        }

        if (hasOuterJoin)
        {
            throw new TdsQueryEngineException($"Outer join is not allowed in the recursive part of a recursive common table expression '{cteName}'.", errorNumber: 462);
        }

        if (CountReferences(member, cteName) != directReferences)
        {
            throw new TdsQueryEngineException("Recursive references are not allowed in subqueries.", errorNumber: 465);
        }

        if (directReferences > 1)
        {
            throw new TdsQueryEngineException($"Recursive member of a common table expression '{cteName}' has multiple recursive references.", errorNumber: 253);
        }
    }

    private static bool ContainsAggregate(SqlSelectClause selectClause)
    {
        // An aggregate of a subquery runs on the rows of the subquery, so it is allowed
        var stack = new Stack<SqlCodeObject>();
        stack.Push(selectClause);
        while (stack.TryPop(out var current))
        {
            if (current is SqlAggregateFunctionCallExpression)
            {
                return true;
            }

            foreach (var child in current.Children)
            {
                if (child is not SqlQueryExpression)
                {
                    stack.Push(child);
                }
            }
        }

        return false;
    }

    /// <summary>Converts the rows of <paramref name="query"/> to <paramref name="targetType"/>, whose members have the same position and the same type, or its nullable version.</summary>
    private static IQueryable ConvertRows(IQueryable query, Type targetType)
    {
        var sourceMembers = GetReadableMembers(query.ElementType);
        var targetMembers = GetReadableMembers(targetType);
        if (sourceMembers.Count != targetMembers.Count)
        {
            throw CreateColumnCountMismatchError();
        }

        var parameter = Expression.Parameter(query.ElementType, "row");
        var bindings = new List<MemberBinding>(targetMembers.Count);
        for (var i = 0; i < targetMembers.Count; i++)
        {
            var value = ConvertExpression(BuildMemberAccess(parameter, sourceMembers[i]), GetMemberType(targetMembers[i]));
            bindings.Add(Expression.Bind(targetMembers[i], value));
        }

        var call = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            [query.ElementType, targetType],
            query.Expression,
            Expression.Quote(Expression.Lambda(Expression.MemberInit(Expression.New(targetType), bindings), parameter)));
        return query.Provider.CreateQuery(call);
    }

    private interface IRecursiveCommonTableExpressionWorkingTable : IEnumerable
    {
        void SetRows(IList rows);
    }

    /// <summary>The rows of the previous recursion level, read by the recursive members.</summary>
    private sealed class RecursiveCommonTableExpressionWorkingTable<T>(string name) : IRecursiveCommonTableExpressionWorkingTable, IEnumerable<T>
    {
        private List<T> _rows = [];

        public void SetRows(IList rows) => _rows = (List<T>)rows;

        public IEnumerator<T> GetEnumerator() => _rows.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Shown in the text of the translated queries
        public override string ToString() => name;
    }

    /// <summary>Evaluates a recursive common table expression each time its rows are enumerated.</summary>
    private sealed class RecursiveCommonTableExpression<T>(
        string name,
        IQueryable anchor,
        IQueryable[] recursiveMembers,
        IRecursiveCommonTableExpressionWorkingTable workingTable,
        int maxRecursion,
        int? maxRows,
        CancellationToken cancellationToken) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator()
        {
            // All the levels are computed before the first row is returned: a query that reads the expression twice,
            // such as a self join, enumerates it again while the first enumeration is in progress, and both would
            // share the working table
            return Evaluate().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => name;

        private List<T> Evaluate()
        {
            var result = new List<T>();
            var level = new List<T>();
            AddRows((IEnumerable<T>)anchor, level, result.Count);
            result.AddRange(level);

            var recursionLevel = 0;
            try
            {
                while (level.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    workingTable.SetRows(level);

                    var nextLevel = new List<T>();
                    foreach (var recursiveMember in recursiveMembers)
                    {
                        AddRows((IEnumerable<T>)recursiveMember, nextLevel, result.Count);
                    }

                    if (nextLevel.Count == 0)
                    {
                        break;
                    }

                    recursionLevel++;
                    if (maxRecursion > 0 && recursionLevel > maxRecursion)
                    {
                        throw new TdsQueryEngineException($"The statement terminated. The maximum recursion {maxRecursion.ToString(CultureInfo.InvariantCulture)} has been exhausted before statement completion.", errorNumber: 530);
                    }

                    result.AddRange(nextLevel);
                    level = nextLevel;
                }
            }
            finally
            {
                workingTable.SetRows(new List<T>());
            }

            return result;
        }

        private void AddRows(IEnumerable<T> rows, List<T> destination, int previousRowCount)
        {
            foreach (var row in rows)
            {
                if (maxRows is not null && previousRowCount + destination.Count >= maxRows.Value)
                {
                    throw new TdsQueryEngineException($"The recursive common table expression '{name}' returned more than {maxRows.Value.ToString(CultureInfo.InvariantCulture)} rows.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                destination.Add(row);
            }
        }
    }
}
