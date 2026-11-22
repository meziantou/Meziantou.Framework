using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>A format the tool reads or writes.</summary>
internal sealed class FileFormat
{
    private FileFormat(string name, FileFormatKind kind, IReadOnlyList<string> extensions, bool canRead, bool canWrite, bool isMultiPage, ImageFormat imageFormat = ImageFormat.Unknown, ArchiveType archiveType = ArchiveType.None)
    {
        Name = name;
        Kind = kind;
        Extensions = extensions;
        ImageFormat = imageFormat;
        ArchiveType = archiveType;
        CanRead = canRead;
        CanWrite = canWrite;
        IsMultiPage = isMultiPage;
    }

    public static FileFormat Pdf { get; } = new("PDF", FileFormatKind.Pdf, [".pdf"], canRead: true, canWrite: true, isMultiPage: true);

    public static FileFormat Epub { get; } = new("EPUB", FileFormatKind.Epub, [".epub"], canRead: true, canWrite: false, isMultiPage: true);

    public static FileFormat Mobi { get; } = new("MOBI", FileFormatKind.Mobi, [".mobi", ".azw3", ".azw", ".prc"], canRead: true, canWrite: false, isMultiPage: true);

    public static FileFormat Markdown { get; } = new("Markdown", FileFormatKind.Markdown, [".md", ".markdown"], canRead: true, canWrite: true, isMultiPage: false);

    public static FileFormat Html { get; } = new("HTML", FileFormatKind.Html, [".html", ".htm"], canRead: true, canWrite: true, isMultiPage: false);

    public static FileFormat Cbz { get; } = Comic("CBZ", [".cbz"], ArchiveType.Zip, canWrite: true);

    public static FileFormat Cbr { get; } = Comic("CBR", [".cbr"], ArchiveType.Rar, canWrite: false);

    public static FileFormat Cb7 { get; } = Comic("CB7", [".cb7", ".c7z"], ArchiveType.SevenZip, canWrite: true);

    public static FileFormat Cbt { get; } = Comic("CBT", [".cbt"], ArchiveType.Tar, canWrite: true);

    public static IReadOnlyList<FileFormat> All { get; } = CreateAll();

    /// <summary>Gets the name of the format, as accepted by <c>--input-format</c> and <c>--output-format</c>.</summary>
    public string Name { get; }

    public FileFormatKind Kind { get; }

    /// <summary>Gets the file extensions of the format, with their leading period (<c>.tar.gz</c> has two parts). The first one is the default extension.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the image format, for the <see cref="FileFormatKind.Image"/> formats.</summary>
    public ImageFormat ImageFormat { get; }

    /// <summary>Gets the container, for the <see cref="FileFormatKind.Archive"/> and <see cref="FileFormatKind.Comic"/> formats.</summary>
    public ArchiveType ArchiveType { get; }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    /// <summary>Gets a value indicating whether a file of the format holds several pages: the pages of every input go to one output file.</summary>
    public bool IsMultiPage { get; }

    public string DefaultExtension => Extensions[0];

    public static FileFormat? FromName(string name)
    {
        foreach (var format in All)
        {
            if (format.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return format;
        }

        // Accept an extension as a name (jpg, .png, tar.gz...)
        return FromExtension(name.StartsWith('.', StringComparison.Ordinal) ? name : "." + name);
    }

    /// <summary>Gets the format of an extension, such as <c>.png</c> or <c>.tar.gz</c>.</summary>
    public static FileFormat? FromExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        foreach (var format in All)
        {
            foreach (var formatExtension in format.Extensions)
            {
                if (formatExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                    return format;
            }
        }

        return null;
    }

    /// <summary>Gets the format of the extension of a file name, the longest that matches: <c>book.tar.gz</c> is a gzip-compressed tar archive.</summary>
    public static FileFormat? FromFileName(string fileName) => GetExtension(fileName) is { } extension ? FromExtension(extension) : null;

    /// <summary>Gets the name of a file without the extension of its format (<c>book</c> for <c>book.tar.gz</c>), or without its last extension when no format matches.</summary>
    public static string GetNameWithoutExtension(string fileName)
    {
        var extension = GetExtension(fileName) ?? Path.GetExtension(fileName);
        return fileName[..^extension.Length];
    }

    /// <summary>Gets the extension of a format a file name ends with, the longest that matches, or null.</summary>
    private static string? GetExtension(string fileName)
    {
        string? result = null;
        foreach (var format in All)
        {
            foreach (var extension in format.Extensions)
            {
                if (fileName.Length > extension.Length && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && extension.Length > (result?.Length ?? 0))
                {
                    result = fileName[^extension.Length..];
                }
            }
        }

        return result;
    }

    public static FileFormat FromImageFormat(ImageFormat imageFormat)
    {
        foreach (var format in All)
        {
            if (format.Kind is FileFormatKind.Image && format.ImageFormat == imageFormat)
                return format;
        }

        throw new ArgumentOutOfRangeException(nameof(imageFormat), imageFormat, "The image format is not supported");
    }

    /// <summary>Gets the archive format of a container, or the comic book format when <paramref name="comic"/> is true and a comic book format uses the container.</summary>
    public static FileFormat FromArchiveType(ArchiveType archiveType, bool comic)
    {
        FileFormat? archiveFormat = null;
        foreach (var format in All)
        {
            if (format.ArchiveType != archiveType)
                continue;

            if ((format.Kind is FileFormatKind.Comic) == comic)
                return format;

            if (format.Kind is FileFormatKind.Archive)
            {
                archiveFormat = format;
            }
        }

        return archiveFormat ?? throw new ArgumentOutOfRangeException(nameof(archiveType), archiveType, "The archive type is not supported");
    }

    public override string ToString() => Name;

    private static FileFormat Comic(string name, string[] extensions, ArchiveType archiveType, bool canWrite)
        => new(name, FileFormatKind.Comic, extensions, canRead: true, canWrite, isMultiPage: true, archiveType: archiveType);

    private static FileFormat Archive(string name, string[] extensions, ArchiveType archiveType, bool canWrite)
        => new(name, FileFormatKind.Archive, extensions, canRead: true, canWrite, isMultiPage: false, archiveType: archiveType);

    private static List<FileFormat> CreateAll()
    {
        var result = new List<FileFormat>();
        foreach (var info in ImageFormatInfo.All)
        {
            if (info.Format is ImageFormat.Unknown or ImageFormat.Custom)
                continue;

            var extensions = info.FileExtensions.ToArray();

            // TIFF is the only image format whose files hold the pages of a document
            var isMultiPage = info.Format is ImageFormat.Tiff;
            result.Add(new FileFormat(info.Format.ToString(), FileFormatKind.Image, extensions, canRead: true, canWrite: true, isMultiPage, imageFormat: info.Format));
        }

        result.Add(Pdf);
        result.Add(Epub);
        result.Add(Mobi);
        result.Add(Markdown);
        result.Add(Html);
        result.Add(Cbz);
        result.Add(Cbr);
        result.Add(Cb7);
        result.Add(Cbt);
        result.Add(Archive("Zip", [".zip"], ArchiveType.Zip, canWrite: true));
        result.Add(Archive("7z", [".7z"], ArchiveType.SevenZip, canWrite: true));
        result.Add(Archive("RAR", [".rar"], ArchiveType.Rar, canWrite: false));
        result.Add(Archive("Tar", [".tar"], ArchiveType.Tar, canWrite: true));
        result.Add(Archive("TarGz", [".tar.gz", ".tgz"], ArchiveType.TarGzip, canWrite: true));
        result.Add(Archive("TarBz2", [".tar.bz2", ".tbz2", ".tbz"], ArchiveType.TarBzip2, canWrite: true));
        result.Add(Archive("TarXz", [".tar.xz", ".txz"], ArchiveType.TarXz, canWrite: true));
        result.Add(Archive("TarLzma", [".tar.lzma", ".tlzma"], ArchiveType.TarLzma, canWrite: true));
        result.Add(Archive("TarLz", [".tar.lz", ".tlz"], ArchiveType.TarLzip, canWrite: true));
        result.Add(Archive("TarZst", [".tar.zst", ".tzst"], ArchiveType.TarZstandard, canWrite: false));
        result.Add(Archive("TarZ", [".tar.Z", ".taz"], ArchiveType.TarUnixCompress, canWrite: false));
        return result;
    }
}
