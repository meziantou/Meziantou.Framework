namespace Meziantou.Framework.Json.Internals;

/// <summary>The declared type system for function expressions (RFC 9535 §2.4.1).</summary>
internal enum FunctionExpressionType
{
    /// <summary>JSON values or Nothing.</summary>
    ValueType,

    /// <summary>LogicalTrue or LogicalFalse.</summary>
    LogicalType,

    /// <summary>Nodelists.</summary>
    NodesType,
}
