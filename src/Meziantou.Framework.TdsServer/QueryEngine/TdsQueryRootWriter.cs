namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>The write handlers of a query root, without their row type.</summary>
internal sealed class TdsQueryRootWriter
{
    private TdsQueryRootWriter(Type rowType, TdsQueryRootUntypedRowsHandler? insert, TdsQueryRootUntypedRowsHandler? update, TdsQueryRootUntypedRowsHandler? delete)
    {
        RowType = rowType;
        Insert = insert;
        Update = update;
        Delete = delete;
    }

    public Type RowType { get; }

    public TdsQueryRootUntypedRowsHandler? Insert { get; }

    public TdsQueryRootUntypedRowsHandler? Update { get; }

    public TdsQueryRootUntypedRowsHandler? Delete { get; }

    public static TdsQueryRootWriter Create<T>(TdsQueryRootWriteHandlers<T> handlers)
    {
        return new TdsQueryRootWriter(typeof(T), Wrap(handlers.Insert), Wrap(handlers.Update), Wrap(handlers.Delete));
    }

    private static TdsQueryRootUntypedRowsHandler? Wrap<T>(TdsQueryRootRowsHandler<T>? handler)
    {
        if (handler is null)
        {
            return null;
        }

        return (context, rows, cancellationToken) => handler(context, rows.Cast<T>().ToArray(), cancellationToken);
    }
}
