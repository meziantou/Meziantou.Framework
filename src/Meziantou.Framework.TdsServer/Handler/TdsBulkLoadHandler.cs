namespace Meziantou.Framework.Tds.Handler;

/// <summary>Handles the bulk loads sent by <c>SqlBulkCopy</c>.</summary>
/// <remarks>
/// A bulk load takes three requests: <c>SqlBulkCopy</c> first asks for the columns of the destination table, which the
/// server answers with <see cref="GetTableColumns"/>, then sends an <c>insert bulk</c> statement, and then the rows,
/// which the server passes to <see cref="LoadRows"/>. None of these requests reach the query handler.
/// </remarks>
public sealed class TdsBulkLoadHandler
{
    /// <summary>Gets the callback that returns the columns of a destination table.</summary>
    public required TdsBulkLoadTableDelegate GetTableColumns { get; init; }

    /// <summary>Gets the callback that receives the rows of a bulk load.</summary>
    public required TdsBulkLoadDelegate LoadRows { get; init; }
}
