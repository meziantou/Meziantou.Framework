namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>The kind of an <see cref="AdfPanel"/>.</summary>
public enum AdfPanelType
{
    /// <summary>A panel type that is not part of the supported schema.</summary>
    Unknown = 0,
    Info,
    Note,
    Tip,
    Warning,
    Error,
    Success,

    /// <summary>A panel with a caller-defined icon and color.</summary>
    Custom,
}
