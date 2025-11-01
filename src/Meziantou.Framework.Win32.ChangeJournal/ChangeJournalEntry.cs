namespace Meziantou.Framework.Win32;

/// <summary>
/// Represents a base class for change journal entries.
/// </summary>
public abstract class ChangeJournalEntry
{
    private static readonly Version Version2 = new(2, 0);
    private static readonly Version Version3 = new(3, 0);
    private static readonly Version Version4 = new(4, 0);

    /// <summary>
    /// <para>The major version number of the change journal software for this record. For example, if the change journal software is version 2.0, the major version number is 2. </para>
    /// <para>This doc was truncated.</para>
    /// <para><see href="https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2#members">Read more on docs.microsoft.com</see>.</para>
    /// </summary>
    public Version Version { get; }

    protected ChangeJournalEntry(ushort majorVersion, ushort minorVersion)
    {
        Version = (majorVersion, minorVersion) switch
        {
            (2, 0) => Version2,
            (3, 0) => Version3,
            (4, 0) => Version4,
            _ => new Version(majorVersion, minorVersion),
        };
    }

    internal abstract Usn GetUsn();
}
