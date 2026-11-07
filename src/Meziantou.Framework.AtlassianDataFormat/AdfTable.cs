namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a table.</summary>
public sealed class AdfTable : AdfNode
{
    /// <inheritdoc />
    public override AdfNodeKind Kind => AdfNodeKind.Table;

    /// <summary>Gets whether the table renders an implicit auto-numbered first column.</summary>
    public bool? IsNumberColumnEnabled { get; init; }

    /// <summary>Gets the table layout.</summary>
    public string? Layout { get; init; }

    /// <summary>Gets the table width, in pixels.</summary>
    public double? Width { get; init; }

    /// <summary>Gets the table display mode.</summary>
    public string? DisplayMode { get; init; }
}
