namespace Meziantou.Framework;

/// <summary>Specifies how unknown HTML elements are handled during conversion.</summary>
public enum UnknownElementHandling
{
    /// <summary>Emit the unknown element as raw HTML.</summary>
    PassThrough,

    /// <summary>Remove the unknown element and its content.</summary>
    Strip,

    /// <summary>Remove the unknown element but keep its content.</summary>
    StripKeepContent,
}
