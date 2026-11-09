using System.Globalization;

namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Exception thrown by a builder in strict mode when the query has diagnostics.</summary>
/// <remarks>
/// <see cref="QueryBuilder{T}.Strict"/> and <see cref="ExpressionQueryBuilder{T}.Strict"/> enable strict mode. Otherwise, the
/// query is read leniently and the diagnostics are available on the built query.
/// </remarks>
public sealed class InvalidQueryException : Exception
{
    public InvalidQueryException()
        : this(message: null)
    {
    }

    public InvalidQueryException(string? message)
        : this(message, innerException: null)
    {
    }

    public InvalidQueryException(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Query = "";
        Diagnostics = [];
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidQueryException"/> class for a query and its diagnostics.</summary>
    /// <param name="query">The query that is not valid.</param>
    /// <param name="diagnostics">The problems found in the query.</param>
    public InvalidQueryException(string query, IReadOnlyList<QueryDiagnostic> diagnostics)
        : base(CreateMessage(diagnostics))
    {
        ArgumentNullException.ThrowIfNull(query);

        Query = query;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the query that is not valid.</summary>
    public string Query { get; }

    /// <summary>Gets the problems found in the query, in the order they appear in it.</summary>
    public IReadOnlyList<QueryDiagnostic> Diagnostics { get; }

    private static string CreateMessage(IReadOnlyList<QueryDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return diagnostics switch
        {
            [] => "The query is not valid",
            [var single] => $"The query is not valid: {single}",
            [var first, ..] => string.Create(CultureInfo.InvariantCulture, $"The query is not valid: {first} (and {diagnostics.Count - 1} more)"),
        };
    }
}
