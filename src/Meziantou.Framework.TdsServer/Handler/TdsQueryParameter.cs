namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents a decoded RPC parameter.</summary>
public sealed class TdsQueryParameter
{
    /// <summary>Gets or sets the parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the decoded parameter value and conversion helpers.</summary>
    public required TdsParameterValue Value { get; init; }
}
