using System.Collections.ObjectModel;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents the response from a query callback.</summary>
public sealed class TdsQueryResult
{
    /// <summary>Gets the informational messages to emit before result sets.</summary>
    public Collection<string> InfoMessages { get; } = [];

    /// <summary>Gets the result sets to serialize.</summary>
    public Collection<TdsResultSet> ResultSets { get; } = [];

    /// <summary>Gets or sets the error to return. When set, result sets, output parameters and the return value are ignored.</summary>
    public TdsQueryError? Error { get; set; }

    /// <summary>
    /// Gets or sets the number of rows affected by the request, as returned by <c>SqlCommand.ExecuteNonQuery</c>.
    /// When <see langword="null"/>, no count is sent and <c>ExecuteNonQuery</c> returns -1. The rows of
    /// <see cref="ResultSets"/> are not counted, as SQL Server does for <c>SELECT</c> statements.
    /// </summary>
    public long? RecordsAffected { get; set; }

    /// <summary>
    /// Gets or sets the return status of a stored procedure, read by a <c>SqlParameter</c> whose direction is
    /// <c>ReturnValue</c>. When <see langword="null"/>, no return status is sent.
    /// </summary>
    public int? ReturnValue { get; set; }

    /// <summary>
    /// Gets the values of the output parameters. The client assigns each value to the <c>Output</c> or
    /// <c>InputOutput</c> parameter with the same name; values without a matching parameter are ignored.
    /// </summary>
    public Collection<TdsOutputParameter> OutputParameters { get; } = [];

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
