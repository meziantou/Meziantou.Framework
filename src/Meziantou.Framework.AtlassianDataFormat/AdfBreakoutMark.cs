namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a breakout applied to a top-level node.</summary>
public sealed class AdfBreakoutMark : AdfMark
{
    /// <inheritdoc />
    public override AdfMarkKind Kind => AdfMarkKind.Breakout;

    /// <summary>Gets the breakout mode, either <c>wide</c> or <c>full-width</c>.</summary>
    public required string Mode { get; init; }

    /// <summary>Gets the breakout width.</summary>
    public double? Width { get; init; }
}
