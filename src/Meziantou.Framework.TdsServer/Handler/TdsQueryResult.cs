using System.Collections.ObjectModel;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents the response from a query callback.</summary>
public sealed class TdsQueryResult
{
    /// <summary>Gets the informational messages to emit before result sets.</summary>
    public Collection<string> InfoMessages { get; } = [];

    /// <summary>Gets the result sets to serialize.</summary>
    public Collection<TdsResultSet> ResultSets { get; } = [];

    /// <summary>Gets or sets the error to return. When set, result sets are ignored.</summary>
    public TdsQueryError? Error { get; set; }

    /// <summary>Creates an error query result.</summary>
    public static TdsQueryResult FromError(TdsQueryError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new TdsQueryResult
        {
            Error = error,
        };
    }
}
