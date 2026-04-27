namespace Meziantou.Framework.Tds.Handler;

/// <summary>Describes the high-level query request type.</summary>
public enum TdsQueryRequestType
{
    /// <summary>A SQL batch request.</summary>
    SqlBatch,

    /// <summary>An RPC request.</summary>
    Rpc,
}
