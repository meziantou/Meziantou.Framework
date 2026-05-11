namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Identifies the query-engine resource being accessed.</summary>
public enum TdsQueryEngineResourceKind
{
    /// <summary>A stored procedure invoked by an RPC request.</summary>
    StoredProcedure,

    /// <summary>A named query root referenced by a SQL query.</summary>
    QueryRoot,
}
