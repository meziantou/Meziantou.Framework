namespace Meziantou.Framework.Email.Storage;

/// <summary>The flags of a message of a Maildir folder: the letters after <c>:2,</c> in the name of its file.</summary>
[Flags]
#pragma warning disable CA1711 // The name of the type is the name of the concept
public enum MaildirFlags
#pragma warning restore CA1711
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The message is a draft (<c>D</c>).</summary>
    Draft = 1 << 0,

    /// <summary>The message is flagged for urgent or special attention (<c>F</c>).</summary>
    Flagged = 1 << 1,

    /// <summary>The message was passed: resent, forwarded or bounced (<c>P</c>).</summary>
    Passed = 1 << 2,

    /// <summary>The message was replied to (<c>R</c>).</summary>
    Replied = 1 << 3,

    /// <summary>The message was read (<c>S</c>, seen).</summary>
    Seen = 1 << 4,

    /// <summary>The message is marked for deletion (<c>T</c>, trashed).</summary>
    Trashed = 1 << 5,
}
