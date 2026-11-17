namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The structure of a DNG file (DNG specification 1.7.1, chapter 2): the first image file directory, which holds the
/// <c>DNGVersion</c> tag, the camera profile and the metadata, and the tree of its SubIFDs, which holds the raw image (the
/// directory whose <c>NewSubFileType</c> is 0) and the previews. Only directories are read here, never pixel data.
/// </summary>
/// <remarks>
/// <para>
/// The tree is walked depth first from IFD 0, each directory once: a directory offset that was already visited is a
/// cycle and is rejected, and at most <see cref="DngFormat.MaxDirectories"/> directories are read. The chain of IFD 0
/// (its next directory) is not part of a DNG file ("SubIFD chains are not supported"): it is not read.
/// </para>
/// </remarks>
internal sealed class DngDocument
{
    private DngDocument(TiffHeader header, TiffDirectory ifd0, byte[] version, byte[] backwardVersion, List<DngDirectory> directories)
    {
        Header = header;
        Ifd0 = ifd0;
        Version = version;
        BackwardVersion = backwardVersion;
        Directories = directories;
    }

    /// <summary>Gets the TIFF header of the file.</summary>
    public TiffHeader Header { get; }

    /// <summary>Gets the first image file directory.</summary>
    public TiffDirectory Ifd0 { get; }

    /// <summary>Gets the four bytes of <c>DNGVersion</c>.</summary>
    public byte[] Version { get; }

    /// <summary>Gets the four bytes of <c>DNGBackwardVersion</c> (the version with its last two bytes set to zero by default).</summary>
    public byte[] BackwardVersion { get; }

    /// <summary>Gets the directories of the tree, IFD 0 first, in the order of a depth-first walk.</summary>
    public IReadOnlyList<DngDirectory> Directories { get; }

    /// <summary>Gets the raw image: the first directory of the tree whose <c>NewSubFileType</c> is 0.</summary>
    public DngDirectory RawImage => Directories.FirstOrDefault(directory => directory.SubfileType == DngFormat.SubfileMain)
        ?? throw DngFormat.Invalid("The DNG file has no main image: no image file directory of its SubIFD tree has a NewSubFileType of 0.");

    /// <summary>Determines whether a directory is the first directory of a DNG file: it has a <c>DNGVersion</c> tag.</summary>
    public static bool IsDng(TiffDirectory ifd0) => ifd0.Find(DngFormat.DngVersionTag) is not null;

    /// <summary>
    /// Determines whether a prefix of a file holds the first directory of a TIFF file and that directory has a
    /// <c>DNGVersion</c> tag (content detection, without random access).
    /// </summary>
    /// <param name="prefix">The first bytes of the data.</param>
    /// <returns><see langword="true"/> when the prefix shows a DNG file; <see langword="false"/> when it shows a TIFF file without the tag, or does not hold the whole first directory.</returns>
    public static bool IsDngPrefix(ReadOnlySpan<byte> prefix)
    {
        if (!TiffHeader.MatchesSignature(prefix))
            return false;

        var bigEndian = prefix[0] == (byte)'M';
        var bigTiff = TiffHeader.ReadUInt16(prefix[2..], bigEndian) == TiffFormat.BigTiffMagic;
        ulong offset = bigTiff ? (prefix.Length >= 16 ? TiffHeader.ReadUInt64(prefix[8..], bigEndian) : ulong.MaxValue) : TiffHeader.ReadUInt32(prefix[4..], bigEndian);
        var countLength = bigTiff ? 8 : 2;
        var entryLength = bigTiff ? TiffFormat.BigTiffEntryLength : TiffFormat.ClassicEntryLength;
        if (offset > (ulong)prefix.Length || (ulong)prefix.Length - offset < (ulong)countLength)
            return false;

        var start = (int)offset;
        var count = bigTiff ? TiffHeader.ReadUInt64(prefix[start..], bigEndian) : TiffHeader.ReadUInt16(prefix[start..], bigEndian);
        for (ulong i = 0; i < count; i++)
        {
            var entry = (long)start + countLength + ((long)i * entryLength);
            if (entry + 2 > prefix.Length)
                return false;

            var tag = TiffHeader.ReadUInt16(prefix[(int)entry..], bigEndian);
            if (tag == DngFormat.DngVersionTag)
                return true;

            // The entries are sorted by tag: a larger tag means that the directory has none
            if (tag > DngFormat.DngVersionTag)
                return false;
        }

        return false;
    }

    /// <summary>Reads the structure of a DNG file.</summary>
    /// <param name="source">The input.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>The structure.</returns>
    /// <exception cref="InvalidImageContentException">The file is not a valid DNG file.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The file requires a newer reader (<c>DNGBackwardVersion</c>).</exception>
    public static DngDocument Read(RandomAccessSource source, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        var header = TiffHeader.Read(source);
        return Read(source, header, TiffDirectory.Read(source, header, header.FirstDirectoryOffset), context);
    }

    /// <summary>Reads the structure of a DNG file whose header and first directory are read.</summary>
    /// <param name="source">The input.</param>
    /// <param name="header">The TIFF header.</param>
    /// <param name="ifd0">The first image file directory.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>The structure.</returns>
    public static DngDocument Read(RandomAccessSource source, TiffHeader header, TiffDirectory ifd0, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ifd0);
        ArgumentNullException.ThrowIfNull(context);
        ifd0.CachesValues = true;
        var version = DngTagReader.TryReadBytes(ifd0, DngFormat.DngVersionTag, "DNGVersion", 4)
            ?? throw DngFormat.Invalid("The first image file directory of the DNG file has no DNGVersion tag.");
        if (version.Length != 4 || version[0] == 0)
            throw DngFormat.Invalid("The DNGVersion tag of the DNG file does not hold a four-tier version number.");

        var backward = DngTagReader.TryReadBytes(ifd0, DngFormat.DngBackwardVersionTag, "DNGBackwardVersion", 4) ?? [version[0], version[1], 0, 0];
        if (backward.Length != 4)
            throw DngFormat.Invalid("The DNGBackwardVersion tag of the DNG file does not hold a four-tier version number.");

        // "A DNG reader should never attempt to process [...] a version higher than DNG specification it was written to support"
        if (DngFormat.CompareVersions(backward, DngFormat.SupportedVersion) > 0)
            throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DNG file requires a reader of version {DngFormat.FormatVersion(backward)} (DNGBackwardVersion); this reader implements version {DngFormat.FormatVersion(DngFormat.SupportedVersion)}."), "DNGBackwardVersion: " + DngFormat.FormatVersion(backward));

        var directories = new List<DngDirectory>();
        var visited = new HashSet<ulong> { header.FirstDirectoryOffset };
        Walk(source, header, ifd0, parent: -1, directories, visited, context);
        return new DngDocument(header, ifd0, version, backward, directories);
    }

    private static void Walk(RandomAccessSource source, TiffHeader header, TiffDirectory directory, int parent, List<DngDirectory> directories, HashSet<ulong> visited, ImageCodecContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        directory.CachesValues = true;
        var subfileType = directory.TryGetUnsigned(TiffFormat.NewSubfileTypeTag, out var value) ? value : DngFormat.SubfileMain;
        var index = directories.Count;
        directories.Add(new DngDirectory(directory, index, parent, subfileType > uint.MaxValue ? uint.MaxValue : (uint)subfileType));
        var children = DngTagReader.TryReadOffsets(directory, TiffFormat.SubIfdsTag, "SubIFDs", DngFormat.MaxDirectories);
        if (children is null)
            return;

        foreach (var offset in children)
        {
            if (!visited.Add(offset))
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The SubIFD tree of the DNG file is cyclic: the directory at offset {offset} is referenced twice."));

            if (directories.Count >= DngFormat.MaxDirectories)
                throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The SubIFD tree of the DNG file has more than {DngFormat.MaxDirectories} image file directories."));

            Walk(source, header, TiffDirectory.Read(source, header, offset), index, directories, visited, context);
        }
    }
}
