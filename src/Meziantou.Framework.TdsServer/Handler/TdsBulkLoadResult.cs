namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents the outcome of a bulk load.</summary>
public sealed class TdsBulkLoadResult
{
    private TdsBulkLoadResult()
    {
    }

    /// <summary>Gets the error to return to the client, or <see langword="null"/> when the rows were accepted.</summary>
    public TdsQueryError? Error { get; private init; }

    /// <summary>Creates a result that accepts the rows. The server reports their count to the client.</summary>
    public static TdsBulkLoadResult Success()
    {
        return new TdsBulkLoadResult();
    }

    /// <summary>Creates a result that rejects the rows: <c>SqlBulkCopy.WriteToServer</c> throws a <c>SqlException</c> with the error.</summary>
    public static TdsBulkLoadResult FromError(TdsQueryError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new TdsBulkLoadResult
        {
            Error = error,
        };
    }
}
