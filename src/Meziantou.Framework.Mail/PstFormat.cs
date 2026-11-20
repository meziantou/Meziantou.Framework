namespace Meziantou.Framework.Mail;

/// <summary>The format of an Outlook data file.</summary>
public enum PstFormat
{
    /// <summary>The ANSI format of Outlook 97 to 2002: 32-bit offsets, files up to 2 GB, strings in a code page.</summary>
    Ansi,

    /// <summary>The Unicode format of Outlook 2003 and later: 64-bit offsets and 512-byte pages.</summary>
    Unicode,

    /// <summary>The Unicode format with 4 KB pages and compressed blocks, used by the offline files (OST) of Outlook 2013 and later.</summary>
    Unicode4K,
}
