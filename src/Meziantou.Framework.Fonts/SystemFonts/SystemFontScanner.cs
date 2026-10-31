using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Fonts;

/// <summary>Finds the font files of directories and reads the names and the style of their faces.</summary>
/// <remarks>
/// <para>
/// An OpenType file or a collection is not loaded: only its table directories and the small tables that describe a face
/// (<c>head</c>, <c>OS/2</c>, <c>post</c>, <c>name</c>, <c>fvar</c>) are read, so that scanning the fonts of a machine
/// does not read gigabytes. A WOFF or WOFF2 file, whose tables are compressed, is loaded and expanded.
/// </para>
/// <para>
/// A file the font code would not load is left out, whole: a face that is malformed, has no outlines nor color glyphs
/// (the rule of the browsers), or a file that cannot be read.
/// </para>
/// </remarks>
internal static class SystemFontScanner
{
    private const int HeaderLength = 12;

    // The largest tables that are read. A name table is a few kilobytes, even in the fonts of many languages.
    private const int MaxNameLength = 4 * 1024 * 1024;
    private const int MaxFvarLength = 1024 * 1024;

    // A character map of every character of Unicode, one group per character, is 13 MiB.
    private const int MaxCmapLength = 16 * 1024 * 1024;

    // OS/2 is at most 100 bytes in its version 5; the fields that are read are in the first 78 bytes.
    private const int MaxOs2Length = 128;

    private const int MaxRecursionDepth = 16;

    private static readonly PooledFontEnvironment CompressedFontEnvironment = new(FontResourceLimits.Default, maxLiveAllocationBytes: 256L * 1024 * 1024);

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        MaxRecursionDepth = MaxRecursionDepth,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = FileAttributes.None,
    };

    /// <summary>Gets the comparer of the paths of the file system of the machine.</summary>
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Scans directories, recursively, and font files.</summary>
    /// <returns>The faces, in the order of <see cref="Compare"/>.</returns>
    public static SystemFontDescriptor[] Scan(IEnumerable<string> paths)
    {
        var files = GetFontFiles(paths);
        var results = new SystemFontDescriptor[]?[files.Count];
        Parallel.For(0, files.Count, i => results[i] = TryReadFile(files[i]));

        var fonts = new List<SystemFontDescriptor>();
        foreach (var result in results)
        {
            if (result is not null)
            {
                fonts.AddRange(result);
            }
        }

        fonts.Sort(Compare);
        return [.. fonts];
    }

    /// <summary>Orders the faces by family, then by file, then by index in the file, so that a scan does not depend on the order of the file system.</summary>
    public static int Compare(SystemFontDescriptor x, SystemFontDescriptor y)
    {
        var result = StringComparer.OrdinalIgnoreCase.Compare(x.FamilyName, y.FamilyName);
        if (result == 0)
        {
            result = StringComparer.Ordinal.Compare(x.FamilyName, y.FamilyName);
        }

        if (result == 0)
        {
            result = StringComparer.Ordinal.Compare(x.FilePath, y.FilePath);
        }

        if (result == 0)
        {
            result = x.FaceIndex.CompareTo(y.FaceIndex);
        }

        return result;
    }

    internal static List<string> GetFontFiles(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(PathComparer);
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                if (seen.Add(fullPath))
                {
                    files.Add(fullPath);
                }

                continue;
            }

            if (!Directory.Exists(fullPath))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(fullPath, "*", EnumerationOptions))
                {
                    if (IsFontFileName(file) && seen.Add(file))
                    {
                        files.Add(file);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // A directory that cannot be listed (removed while it is scanned, a network volume that is gone) has no font
            }
        }

        return files;
    }

    internal static bool IsFontFileName(string path)
    {
        var extension = Path.GetExtension(path.AsSpan());
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads the faces of a file, or returns <see langword="null"/> when the file is not a font the font code loads.</summary>
    internal static SystemFontDescriptor[]? TryReadFile(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
            var length = RandomAccess.GetLength(handle);
            if (length < HeaderLength)
                return null;

            Span<byte> header = stackalloc byte[HeaderLength];
            ReadExactly(handle, header, 0);
            switch (FontBinary.UInt32(header, 0))
            {
                case FontTags.TrueType or FontTags.AppleTrueType or FontTags.OpenTypeCff:
                    return [ReadFace(new FileSource(handle, length), path, 0, 0)];

                case FontTags.Collection:
                    return ReadCollection(new FileSource(handle, length), path, header);

                case FontTags.Woff or FontTags.Woff2:
                    return ReadCompressed(handle, length, path);

                default:
                    return null;
            }
        }
        catch (Exception exception) when (exception is FontException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static SystemFontDescriptor[] ReadCompressed(SafeFileHandle handle, long length, string path)
    {
        if (length > CompressedFontEnvironment.Limits.MaxEncodedBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.EncodedBytes, CompressedFontEnvironment.Limits.MaxEncodedBytes, length);

        var bytes = new byte[length];
        ReadExactly(handle, bytes, 0);

        // The faces share the bytes of the font the container held: a font, or a collection.
        var font = FontLoader.Load(bytes, CompressedFontEnvironment)[0].Data.RawBytes;
        var source = new FileSource(font);
        return FontBinary.UInt32(font, 0) == FontTags.Collection
            ? ReadCollection(source, path, font.AsSpan(0, HeaderLength))
            : [ReadFace(source, path, 0, 0)];
    }

    /// <summary>Reads the faces of a font collection: OpenType specification, "Font Collections".</summary>
    private static SystemFontDescriptor[] ReadCollection(FileSource source, string path, ReadOnlySpan<byte> header)
    {
        var offsets = ReadCollectionOffsets(source, header, out var count);
        var faces = new SystemFontDescriptor[count];
        for (var i = 0; i < count; i++)
        {
            faces[i] = ReadFace(source, path, FontBinary.UInt32(offsets, i * 4), i);
        }

        return faces;
    }

    /// <summary>Reads the offsets of the faces of a font collection: OpenType specification, "Font Collections".</summary>
    private static byte[] ReadCollectionOffsets(FileSource source, ReadOnlySpan<byte> header, out int count)
    {
        var majorVersion = FontBinary.UInt16(header, 4);
        if (majorVersion is not (1 or 2))
            throw FontErrors.Invalid($"the font collection has the unknown version {majorVersion}.");

        count = FontBinary.Offset32(header, 8);
        if (count == 0)
            throw FontErrors.Invalid("the font collection has no font.");

        var maxFaces = FontResourceLimits.Default.MaxCollectionFaces;
        if (count > maxFaces)
            throw new FontResourceLimitException(FontResourceLimitKind.CollectionFaces, maxFaces, count);

        return source.Read(HeaderLength, count * 4);
    }

    /// <summary>
    /// Reads the characters a face of a file has a glyph for, from its character map, or returns
    /// <see cref="SystemFontCoverage.Empty"/> when the file cannot be read or the character map is malformed.
    /// </summary>
    /// <remarks>Only the table directory of the face, its <c>maxp</c> table and its <c>cmap</c> table are read; a WOFF or WOFF2 file is expanded.</remarks>
    internal static SystemFontCoverage ReadCoverage(string path, int faceIndex)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
            var length = RandomAccess.GetLength(handle);
            if (length < HeaderLength)
                return SystemFontCoverage.Empty;

            Span<byte> header = stackalloc byte[HeaderLength];
            ReadExactly(handle, header, 0);
            switch (FontBinary.UInt32(header, 0))
            {
                case FontTags.TrueType or FontTags.AppleTrueType or FontTags.OpenTypeCff:
                    return faceIndex == 0 ? ReadFaceCoverage(new FileSource(handle, length), 0) : SystemFontCoverage.Empty;

                case FontTags.Collection:
                    return ReadCollectionCoverage(new FileSource(handle, length), header, faceIndex);

                case FontTags.Woff or FontTags.Woff2:
                    {
                        if (length > CompressedFontEnvironment.Limits.MaxEncodedBytes)
                            return SystemFontCoverage.Empty;

                        var bytes = new byte[length];
                        ReadExactly(handle, bytes, 0);
                        var font = FontLoader.Load(bytes, CompressedFontEnvironment)[0].Data.RawBytes;
                        var source = new FileSource(font);
                        if (FontBinary.UInt32(font, 0) == FontTags.Collection)
                            return ReadCollectionCoverage(source, font.AsSpan(0, HeaderLength), faceIndex);

                        return faceIndex == 0 ? ReadFaceCoverage(source, 0) : SystemFontCoverage.Empty;
                    }

                default:
                    return SystemFontCoverage.Empty;
            }
        }
        catch (Exception exception) when (exception is FontException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return SystemFontCoverage.Empty;
        }
    }

    private static SystemFontCoverage ReadCollectionCoverage(FileSource source, ReadOnlySpan<byte> header, int faceIndex)
    {
        var offsets = ReadCollectionOffsets(source, header, out var count);
        return faceIndex >= 0 && faceIndex < count ? ReadFaceCoverage(source, FontBinary.UInt32(offsets, faceIndex * 4)) : SystemFontCoverage.Empty;
    }

    private static SystemFontCoverage ReadFaceCoverage(FileSource source, long offset)
    {
        var directoryLength = SfntDirectory.GetLength(source.Read(offset, HeaderLength));
        var directory = SfntDirectory.ReadDirectory(source.Read(offset, directoryLength), source.Length);

        // maxp: the number of glyphs, which bounds the glyphs of the character map.
        var maxp = ReadTable(source, directory, FontTags.Maxp, maxLength: 6);
        if (maxp.Length < 6)
            throw FontErrors.InvalidTable(FontTags.Maxp, "is too short.");

        var cmap = ReadTable(source, directory, FontTags.Cmap, MaxCmapLength, whole: true);
        if (cmap.Length == 0)
            return SystemFontCoverage.Empty;

        return new SystemFontCoverage(CmapTable.Read(cmap, FontBinary.UInt16(maxp, 4)).GetCoveredRanges());
    }

    /// <summary>Reads the names and the style of a face, and checks what the font code checks of the tables that are read.</summary>
    private static SystemFontDescriptor ReadFace(FileSource source, string path, long offset, int faceIndex)
    {
        var directoryLength = SfntDirectory.GetLength(source.Read(offset, HeaderLength));
        var directory = SfntDirectory.ReadDirectory(source.Read(offset, directoryLength), source.Length);

        // The tables every face has, and outlines or color glyphs (the rule of WebPlatform's FontFile).
        if (!Has(directory, FontTags.Head) || !Has(directory, FontTags.Maxp) || !Has(directory, FontTags.Hhea) || !Has(directory, FontTags.Hmtx) || !Has(directory, FontTags.Cmap))
            throw FontErrors.Invalid("a face does not have the required tables.");

        if (!(Has(directory, FontTags.Glyf) && Has(directory, FontTags.Loca)) && !Has(directory, FontTags.Cff) && !Has(directory, FontTags.Cff2)
            && !Has(directory, FontTags.Cbdt) && !Has(directory, FontTags.Sbix) && !Has(directory, FontTags.Svg))
        {
            throw FontErrors.Invalid("a face has no outlines nor color glyphs.");
        }

        // head: OpenType specification, "head - Font Header Table".
        var head = ReadTable(source, directory, FontTags.Head, maxLength: 54);
        if (head.Length < 54 || FontBinary.UInt32(head, 12) != 0x5F0F3CF5)
            throw FontErrors.InvalidTable(FontTags.Head, "does not have its magic number.");

        var unitsPerEm = FontBinary.UInt16(head, 18);
        if (unitsPerEm is < 16 or > 16384)
            throw FontErrors.InvalidTable(FontTags.Head, $"declares {unitsPerEm} units per em; the valid range is 16 to 16384.");

        var macStyle = FontBinary.UInt16(head, 44);
        var os2 = ReadTable(source, directory, FontTags.Os2, MaxOs2Length);
        var post = ReadTable(source, directory, FontTags.Post, FaceStyle.MinPostLength);
        var name = ReadTable(source, directory, FontTags.Name, MaxNameLength, whole: true);
        var fvar = ReadTable(source, directory, FontTags.Fvar, MaxFvarLength, whole: true);
        var style = FaceStyle.Read(macStyle, os2, post, name);

        string? fullName = null;
        var familyNames = new List<string>();
        if (name.Length >= FaceStyle.MinNameLength)
        {
            fullName = NameTable.Find(name, NameTable.FullName);
            NameTable.FindAll(name, NameTable.TypographicFamilyName, familyNames);
            NameTable.FindAll(name, NameTable.FamilyName, familyNames);
        }

        var axes = FvarTable.Read(fvar)?.Axes ?? [];
        return new SystemFontDescriptor(path, faceIndex, style, [.. familyNames.Distinct(StringComparer.OrdinalIgnoreCase)], fullName, macStyle, axes);
    }

    private static bool Has(SfntDirectory directory, uint tag) => directory.TryFind(tag, out _, out _);

    /// <summary>
    /// Reads the first <paramref name="maxLength"/> bytes of a table, or returns an empty table when the face does not
    /// have it. When <paramref name="whole"/> is set, the whole table is needed: a longer table is an error.
    /// </summary>
    private static byte[] ReadTable(FileSource source, SfntDirectory directory, uint tag, int maxLength, bool whole = false)
    {
        if (!directory.TryFind(tag, out var offset, out var length))
            return [];

        if (length > maxLength)
        {
            if (whole)
                throw FontErrors.InvalidTable(tag, $"is longer than {maxLength} bytes.");

            length = maxLength;
        }

        return source.Read(offset, length);
    }

    private static void ReadExactly(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        while (!buffer.IsEmpty)
        {
            var read = RandomAccess.Read(handle, buffer, offset);
            if (read == 0)
                throw new EndOfStreamException("The font file is shorter than its length.");

            buffer = buffer[read..];
            offset += read;
        }
    }

    /// <summary>The bytes of a font: a file read on demand, or the font a WOFF or WOFF2 file held.</summary>
    private readonly struct FileSource
    {
        private readonly SafeFileHandle? _handle;
        private readonly byte[]? _bytes;

        public FileSource(SafeFileHandle handle, long length)
        {
            _handle = handle;
            Length = length;
        }

        public FileSource(byte[] bytes)
        {
            _bytes = bytes;
            Length = bytes.Length;
        }

        public long Length { get; }

        public byte[] Read(long offset, int length)
        {
            if (offset < 0 || offset + length > Length)
                throw FontErrors.Invalid("a structure lies outside the file.");

            if (_bytes is not null)
                return _bytes.AsSpan((int)offset, length).ToArray();

            var buffer = new byte[length];
            ReadExactly(_handle!, buffer, offset);
            return buffer;
        }
    }
}
