namespace Meziantou.Framework.CodeDom;

/// <summary>Specifies the direction of a method argument.</summary>
public enum Direction
{
    /// <summary>Input parameter (in).</summary>
    In,

    /// <summary>Output parameter (out).</summary>
    Out,

    /// <summary>Reference parameter (ref).</summary>
    InOut,

    /// <summary>Read-only reference parameter (in readonly ref).</summary>
    ReadOnlyRef,
}
