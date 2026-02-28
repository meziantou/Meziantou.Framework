namespace Meziantou.Framework;

/// <summary>Represents supported BCrypt hash revisions.</summary>
public enum BcryptVersion
{
    /// <summary>Legacy revision represented as <c>$2$</c>.</summary>
    Revision2,

    /// <summary>Revision represented as <c>$2a$</c>.</summary>
    Revision2A,

    /// <summary>Revision represented as <c>$2b$</c>.</summary>
    Revision2B,

    /// <summary>Revision represented as <c>$2x$</c>.</summary>
    Revision2X,

    /// <summary>Revision represented as <c>$2y$</c>.</summary>
    Revision2Y,
}