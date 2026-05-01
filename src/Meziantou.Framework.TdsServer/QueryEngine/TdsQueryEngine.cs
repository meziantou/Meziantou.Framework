using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Creates TDS query handlers backed by the built-in query engine.</summary>
public static class TdsQueryEngine
{
    /// <summary>Creates a query delegate from query engine options.</summary>
    public static TdsQueryDelegate CreateQueryHandler(TdsQueryEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var executor = new TdsQueryEngineExecutor(options);
        return executor.ExecuteAsync;
    }
}
