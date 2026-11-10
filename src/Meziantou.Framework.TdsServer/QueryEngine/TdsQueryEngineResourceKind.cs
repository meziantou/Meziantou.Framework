namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Identifies the query-engine resource being accessed.</summary>
public enum TdsQueryEngineResourceKind
{
    /// <summary>A stored procedure invoked by an RPC request.</summary>
    StoredProcedure,

    /// <summary>A named query root referenced by a SQL query.</summary>
    QueryRoot,

    /// <summary>A named query root targeted by an <c>INSERT</c> statement.</summary>
    QueryRootInsert,

    /// <summary>A named query root targeted by an <c>UPDATE</c> statement.</summary>
    QueryRootUpdate,

    /// <summary>A named query root targeted by a <c>DELETE</c> statement.</summary>
    QueryRootDelete,
}
