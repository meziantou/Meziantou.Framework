namespace Meziantou.Framework.Email.Storage;

/// <summary>The flags of a message of Apple Mail: the low bits of the <c>flags</c> property of an .emlx file. Apple does not document them; these are the bits other tools read.</summary>
[Flags]
#pragma warning disable CA1711 // The name of the type is the name of the concept
public enum EmlxFlags
#pragma warning restore CA1711
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The message was read.</summary>
    Read = 1 << 0,

    /// <summary>The message is deleted.</summary>
    Deleted = 1 << 1,

    /// <summary>The message was answered.</summary>
    Answered = 1 << 2,

    /// <summary>The message is encrypted.</summary>
    Encrypted = 1 << 3,

    /// <summary>The message is flagged.</summary>
    Flagged = 1 << 4,

    /// <summary>The message is recent.</summary>
    Recent = 1 << 5,

    /// <summary>The message is a draft.</summary>
    Draft = 1 << 6,

    /// <summary>The message was not downloaded from a server.</summary>
    Initial = 1 << 7,

    /// <summary>The message was forwarded.</summary>
    Forwarded = 1 << 8,

    /// <summary>The message was redirected.</summary>
    Redirected = 1 << 9,
}
