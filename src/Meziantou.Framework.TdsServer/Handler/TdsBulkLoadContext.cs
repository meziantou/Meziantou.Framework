using System.Net;
using System.Security.Claims;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Provides the rows of a bulk load.</summary>
public sealed class TdsBulkLoadContext
{
    /// <summary>Gets the remote endpoint of the client.</summary>
    public required EndPoint RemoteEndPoint { get; init; }

    /// <summary>Gets the authenticated user context associated with the connection.</summary>
    public ClaimsPrincipal? UserContext { get; init; }

    /// <summary>Gets the name of the destination table, without its schema and quotes, such as <c>Orders</c> for <c>[dbo].[Orders]</c>.</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the schema of the destination table, such as <c>dbo</c>, or <see langword="null"/> when the client did not specify one.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Gets the database of the destination table, or <see langword="null"/> when the client did not specify one.</summary>
    public string? DatabaseName { get; init; }

    /// <summary>Gets the columns the client sends, in the order of the values of <see cref="Rows"/>.</summary>
    /// <remarks>
    /// These are the columns of <see cref="TdsBulkLoadHandler.GetTableColumns"/> that the column mappings of
    /// <c>SqlBulkCopy</c> target, so a column the client does not copy is missing.
    /// </remarks>
    public required IReadOnlyList<TdsColumn> Columns { get; init; }

    /// <summary>Gets the rows, one value per column of <see cref="Columns"/>. A SQL NULL is <see langword="null"/>.</summary>
    /// <remarks>
    /// A value has the .NET type of its column, as the query parameters have: <see cref="int"/> for
    /// <see cref="TdsColumnType.Int32"/>, <see cref="string"/> for <see cref="TdsColumnType.NVarChar"/>, and so on.
    /// </remarks>
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }

    /// <summary>Gets the options of the <c>insert bulk</c> statement, such as <c>TABLOCK</c>, <c>KEEP_NULLS</c> or <c>ORDER([Id] ASC)</c>.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}
