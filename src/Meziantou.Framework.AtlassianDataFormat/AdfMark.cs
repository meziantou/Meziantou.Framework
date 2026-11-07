namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a mark applied to an <see cref="AdfNode"/>.</summary>
public abstract class AdfMark
{
    /// <summary>Gets the type of the mark.</summary>
    public abstract AdfMarkKind Kind { get; }
}
