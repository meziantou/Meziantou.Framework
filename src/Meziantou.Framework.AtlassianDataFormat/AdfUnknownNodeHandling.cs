namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How nodes whose type is not part of the supported schema are converted.</summary>
public enum AdfUnknownNodeHandling
{
    /// <summary>The node and its content are dropped.</summary>
    Skip = 0,

    /// <summary>The node is dropped but its content is converted.</summary>
    KeepContent,

    /// <summary>An <see cref="AdfException"/> is thrown.</summary>
    Throw,
}
