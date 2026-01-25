using System.Linq.Expressions;

namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Represents a handler for unhandled properties that returns an expression.</summary>
/// <typeparam name="T">The type of object to query against.</typeparam>
/// <param name="propertyName">The name of the property in the query.</param>
/// <param name="operator">The comparison operator used in the query.</param>
/// <param name="value">The value to compare against.</param>
/// <returns>An expression that filters items, or <see langword="null"/> to treat as free-text.</returns>
public delegate Expression<Func<T, bool>>? UnhandledPropertyExpressionHandler<T>(string propertyName, KeyValueOperator @operator, string value);
