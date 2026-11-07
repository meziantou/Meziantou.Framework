namespace Meziantou.Framework.Json;

/// <summary>The argument and result types of RFC 9535 filter functions.</summary>
public enum JsonPathFunctionType
{
    /// <summary>A JSON value or Nothing (distinct from JSON null).</summary>
    Value,
    /// <summary>A logical true or false result.</summary>
    Logical,
    /// <summary>An ordered list of nodes, including duplicates.</summary>
    Nodes,
}
