using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Creates TDS query handlers backed by the built-in query engine.</summary>
public static class TdsQueryEngine
{
    internal const string RequiresUnreferencedCodeMessage = "The query engine translates SQL into LINQ expression trees and reads the members of the query roots and stored procedures with reflection.";
    internal const string RequiresDynamicCodeMessage = "The query engine compiles LINQ expression trees and creates projection types and generic instantiations at run time.";

    /// <summary>Creates a query delegate from query engine options.</summary>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(RequiresDynamicCodeMessage)]
    public static TdsQueryDelegate CreateQueryHandler(TdsQueryEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var executor = new TdsQueryEngineExecutor(options);
        return executor.ExecuteAsync;
    }
}
