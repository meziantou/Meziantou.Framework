namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Handles the <c>INSERT</c>, <c>UPDATE</c> and <c>DELETE</c> statements that target a query root.</summary>
/// <typeparam name="T">The type of the rows of the query root.</typeparam>
/// <remarks>A statement whose handler is <see langword="null"/> returns an error.</remarks>
public sealed class TdsQueryRootWriteHandlers<T>
{
    /// <summary>
    /// Gets or sets the handler of <c>INSERT</c> statements. It receives the new rows, created with the public
    /// parameterless constructor of <typeparamref name="T"/> and the values of the statement.
    /// </summary>
    public TdsQueryRootRowsHandler<T>? Insert { get; set; }

    /// <summary>
    /// Gets or sets the handler of <c>UPDATE</c> statements. It receives the rows returned by the query root that match
    /// the statement, after the engine has assigned their new values.
    /// </summary>
    public TdsQueryRootRowsHandler<T>? Update { get; set; }

    /// <summary>Gets or sets the handler of <c>DELETE</c> statements. It receives the rows returned by the query root that match the statement.</summary>
    public TdsQueryRootRowsHandler<T>? Delete { get; set; }
}
