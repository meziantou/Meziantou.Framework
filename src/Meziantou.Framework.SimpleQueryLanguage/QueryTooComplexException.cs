namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Exception thrown when a query is too complex to be parsed or evaluated.</summary>
/// <remarks>
/// A query can be too complex because it nests expressions too deeply, or because converting it to
/// disjunctive normal form would produce an impractical number of terms. Queries coming from an
/// untrusted source should be built inside a <see langword="try"/> block that handles this exception.
/// </remarks>
public sealed class QueryTooComplexException : Exception
{
    public QueryTooComplexException()
    {
    }

    public QueryTooComplexException(string? message)
        : base(message)
    {
    }

    public QueryTooComplexException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
