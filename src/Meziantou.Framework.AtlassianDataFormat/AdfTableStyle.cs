namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How an <see cref="AdfTable"/> is converted.</summary>
public enum AdfTableStyle
{
    /// <summary>A GitHub pipe table. Cells holding more than inline content are flattened, separated by <c>&lt;br&gt;</c>.</summary>
    PipeTable = 0,

    /// <summary>An HTML table, which preserves cells spanning several rows or columns.</summary>
    Html,

    /// <summary>A pipe table when every cell holds only inline content and no cell spans several rows or columns, an HTML table otherwise.</summary>
    Auto,
}
