using System.Runtime.InteropServices;
using Windows.Win32.System.Ioctl;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a modified extent (range) in a version 4 change journal entry.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ChangeJournalEntryExtent
{
    internal ChangeJournalEntryExtent(USN_RECORD_EXTENT extent)
    {
        Offset = extent.Offset;
        Length = extent.Length;
    }

    /// <summary>The offset of the extent, in bytes.</summary>
    public long Offset { get; }

    /// <summary>The length of the extent, in bytes.</summary>
    public long Length { get; }

    public override string ToString() => $"Offset: {Offset}, Length: {Length}";
}
