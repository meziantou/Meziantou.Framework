using System.Formats.Tar;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Tar;

/// <summary>An entry of a tar archive read by <see cref="TarReader"/>.</summary>
internal sealed class TarEntryAdapter : ArchiveEntry
{
    private readonly Func<TarEntryAdapter, Stream> _open;

    private TarEntryAdapter(TarEntry entry, Func<TarEntryAdapter, Stream> open)
    {
        _open = open;
        EntryType = entry.EntryType;
        FullName = entry.Name;
        Length = IsRegularFile(entry.EntryType) ? entry.Length : 0;
        LastWriteTime = entry.ModificationTime;
        UnixFileMode = entry.Mode;
        LinkTarget = entry.EntryType is TarEntryType.SymbolicLink ? entry.LinkName : null;
        HardLinkTarget = entry.EntryType is TarEntryType.HardLink ? entry.LinkName : null;
        if (entry is PaxTarEntry or GnuTarEntry)
        {
            var (access, creation) = entry switch
            {
                PaxTarEntry pax => (GetPaxTime(pax, "atime"), GetPaxTime(pax, "ctime")),
                GnuTarEntry gnu => (gnu.AccessTime == default ? (DateTimeOffset?)null : gnu.AccessTime, (DateTimeOffset?)null),
                _ => (null, null),
            };
            LastAccessTime = access;
            CreationTime = creation;
        }
    }

    public TarEntryType EntryType { get; }

    /// <summary>Gets or sets the offset of the content in a seekable archive, for random access.</summary>
    public long DataOffset { get; set; } = -1;

    public override string FullName { get; }

    public override long Length { get; }

    public override long CompressedLength => Length;

    public override DateTimeOffset? LastWriteTime { get; }

    public override DateTimeOffset? CreationTime { get; }

    public override DateTimeOffset? LastAccessTime { get; }

    public override bool IsDirectory => EntryType is TarEntryType.Directory;

    public override string? LinkTarget { get; }

    public override string? HardLinkTarget { get; }

    public override UnixFileMode? UnixFileMode { get; }

    public override Stream Open()
    {
        if (IsDirectory)
            throw new InvalidOperationException("The entry is a directory.");

        return _open(this);
    }

    /// <summary>Creates the entry of a tar entry, or returns <see langword="null"/> for an entry that is not a file, a directory or a link (devices, FIFOs).</summary>
    public static TarEntryAdapter? Create(TarEntry entry, Func<TarEntryAdapter, Stream> open)
    {
        return entry.EntryType switch
        {
            TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile or TarEntryType.Directory or TarEntryType.SymbolicLink or TarEntryType.HardLink => new TarEntryAdapter(entry, open),
            _ => null,
        };
    }

    public static bool IsRegularFile(TarEntryType type) => type is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;

    private static DateTimeOffset? GetPaxTime(PaxTarEntry entry, string name)
    {
        if (!entry.ExtendedAttributes.TryGetValue(name, out var value))
            return null;

        if (!decimal.TryParse(value, System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            return null;

        if (seconds < -62135596800m || seconds > 253402300799m)
            return null;

        return DateTimeOffset.UnixEpoch.AddTicks((long)(seconds * TimeSpan.TicksPerSecond));
    }

    internal static Stream EmptyStream() => new MemoryStream([], writable: false);

    internal static Stream Window(Stream archive, TarEntryAdapter entry)
    {
        if (entry.Length is 0 || entry.DataOffset < 0)
            return EmptyStream();

        return new BoundedReadStream(archive, entry.DataOffset, entry.Length);
    }
}
