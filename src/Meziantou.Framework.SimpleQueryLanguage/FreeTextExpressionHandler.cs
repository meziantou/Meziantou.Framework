using System.Linq.Expressions;

namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Represents a handler for free-text queries that returns an expression.</summary>
/// <typeparam name="T">The type of object to query against.</typeparam>
/// <param name="text">The free-text search term.</param>
/// <returns>An expression that filters items matching the text.</returns>
public delegate Expression<Func<T, bool>> FreeTextExpressionHandler<T>(string text);
