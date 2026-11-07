using System.Collections.ObjectModel;

namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Represents the response from a query callback.</summary>
public sealed class PostgreSqlQueryResult
{
    /// <summary>Gets informational notices to emit before result sets.</summary>
    public Collection<string> Notices { get; } = [];

    /// <summary>Gets the result sets to serialize.</summary>
    public Collection<PostgreSqlResultSet> ResultSets { get; } = [];

    /// <summary>Gets or sets the command completion tag. Defaults to an inferred value when omitted.</summary>
    public string? CommandTag { get; set; }

    /// <summary>Gets or sets the error to return. When set, result sets are ignored.</summary>
    public PostgreSqlQueryError? Error { get; set; }

    /// <summary>
    /// Gets or sets the number of rows affected, reported in the command completion tag.
    /// When <see langword="null"/>, the number of rows in the result set is used, or zero when there is none.
    /// </summary>
    public int? AffectedRowCount { get; set; }

    /// <summary>Gets or sets the transaction state reported to the client after this command.</summary>
    public PostgreSqlTransactionStatus TransactionStatus { get; set; }

    /// <summary>Creates an error query result.</summary>
    public static PostgreSqlQueryResult FromError(PostgreSqlQueryError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new PostgreSqlQueryResult
        {
            Error = error,
        };
    }
}
