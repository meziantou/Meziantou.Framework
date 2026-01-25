using System.Linq.Expressions;

namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Represents a compiled query that can be applied to an <see cref="IQueryable{T}"/>.</summary>
/// <typeparam name="T">The type of object to query against.</typeparam>
/// <remarks>
/// This query type is designed for use with Entity Framework Core or other LINQ providers
/// that translate expressions to SQL or other query languages.
/// </remarks>
/// <example>
/// <code>
/// var queryBuilder = new ExpressionQueryBuilder&lt;Person&gt;();
/// queryBuilder.AddHandler("name", item => item.FullName);
/// var query = queryBuilder.Build("name:john");
/// var results = await dbContext.People.Apply(query).ToListAsync();
/// </code>
/// </example>
public sealed class ExpressionQuery<T>
{
    private readonly Expression<Func<T, bool>>? _predicate;

    /// <summary>Gets the original query text.</summary>
    public string Text { get; }

    internal ExpressionQuery(string text, Expression<Func<T, bool>>? predicate)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _predicate = predicate;
    }

    /// <summary>Gets the compiled predicate expression, or <see langword="null"/> if the query matches all items.</summary>
    public Expression<Func<T, bool>>? Predicate => _predicate;

    /// <summary>Applies the query to an <see cref="IQueryable{T}"/>.</summary>
    /// <param name="queryable">The queryable to filter.</param>
    /// <returns>A filtered queryable.</returns>
    public IQueryable<T> Apply(IQueryable<T> queryable)
    {
        ArgumentNullException.ThrowIfNull(queryable);

        if (_predicate is null)
            return queryable;

        return queryable.Where(_predicate);
    }
}
