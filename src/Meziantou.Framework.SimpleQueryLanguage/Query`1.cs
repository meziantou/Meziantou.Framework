namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Represents a compiled query that can be evaluated against objects.</summary>
/// <typeparam name="T">The type of object to query against.</typeparam>
/// <example>
/// <code>
/// var queryBuilder = new QueryBuilder&lt;Person&gt;();
/// queryBuilder.AddHandler&lt;string&gt;("name", (obj, value) => obj.FullName.Contains(value));
/// var query = queryBuilder.Build("name:john");
/// bool matches = query.Evaluate(new Person("John Doe", DateTime.Now));
/// </code>
/// </example>
public sealed class Query<T>
{
    private readonly Predicate<T> _predicate;

    /// <summary>Gets the original query text.</summary>
    public string Text { get; }

    internal Query(string text, Predicate<T> predicate)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <summary>Evaluates the query against an object.</summary>
    /// <param name="value">The object to evaluate.</param>
    /// <returns><see langword="true"/> if the object matches the query; otherwise, <see langword="false"/>.</returns>
    public bool Evaluate(T value)
    {
        return _predicate(value);
    }
}
