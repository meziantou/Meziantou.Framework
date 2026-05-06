using System.Linq.Expressions;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>
/// Builds an expression for a scalar SQL function call from its translated arguments.
/// </summary>
/// <param name="arguments">Function arguments translated to LINQ expressions.</param>
/// <returns>The expression that represents the function call.</returns>
public delegate Expression TdsQueryScalarFunction(IReadOnlyList<Expression> arguments);
