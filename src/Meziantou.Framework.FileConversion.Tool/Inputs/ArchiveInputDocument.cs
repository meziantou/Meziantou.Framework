using Meziantou.Framework.Compression;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool.Inputs;

/// <summary>A comic book archive (CBZ, CBR, CB7, CBT), or any archive: its images, sorted by name, are the pages.</summary>
internal sealed class ArchiveInputDocument : InputDocument
{
    private readonly Archive _archive;
    private readonly ArchiveEntry[] _entries;

    private ArchiveInputDocument(FullPath path, FileFormat format, Archive archive, ArchiveEntry[] entries)
        : base(path, format)
    {
        _archive = archive;
        _entries = entries;
    }

    public override int? PageCount => _entries.Length;

    public static ArchiveInputDocument Open(FullPath path, FileFormat format, ConversionSettings settings)
    {
        var archive = ArchiveFiles.Open(path, settings);
        try
        {
            return new ArchiveInputDocument(path, format, archive, [.. GetPageEntries(archive)]);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>Gets the entries of the images of an archive, sorted by their names in natural order: the files of the folders created by macOS and the hidden files are ignored.</summary>
    /// <exception cref="InvalidDataException">The archive has more entries than <see cref="ZipFiles.MaxEntryCount"/>.</exception>
    public static IEnumerable<ArchiveEntry> GetPageEntries(Archive archive)
    {
        if (archive.Entries.Count > ZipFiles.MaxEntryCount)
            throw new InvalidDataException($"The archive has more than {ZipFiles.MaxEntryCount.ToString(CultureInfo.InvariantCulture)} entries");

        return archive.Entries
            .Where(IsPageEntry)
            .OrderBy(entry => entry.FullName, NaturalStringComparer.Instance);
    }

    public override IEnumerable<ConversionPage> GetPages(CancellationToken cancellationToken)
    {
        for (var i = 0; i < _entries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var data = ArchiveFiles.ReadEntry(_entries[i]);
            var format = Image.DetectFormat(data);
            if (format is ImageFormat.Unknown)
                throw new InvalidDataException($"The entry '{_entries[i].FullName}' of the archive is not an image of a supported format");

            yield return new EncodedImagePage(i + 1, new EncodedImage(data, format));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _archive.Dispose();
        }

        base.Dispose(disposing);
    }

    private static bool IsPageEntry(ArchiveEntry entry)
    {
        if (entry.IsDirectory || entry.LinkTarget is not null || entry.HardLinkTarget is not null)
            return false;

        if (entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal) || entry.Name.StartsWith('.', StringComparison.Ordinal))
            return false;

        return FileFormat.FromExtension(System.IO.Path.GetExtension(entry.Name)) is { Kind: FileFormatKind.Image };
    }
}
