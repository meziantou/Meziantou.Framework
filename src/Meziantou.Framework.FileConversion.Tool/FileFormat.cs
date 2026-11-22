using Meziantou.Framework.Email.Storage;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>A format the tool reads or writes.</summary>
internal sealed class FileFormat
{
    private FileFormat(string name, FileFormatKind kind, IReadOnlyList<string> extensions, bool canRead, bool canWrite, bool isMultiPage, ImageFormat imageFormat = ImageFormat.Unknown, ArchiveType archiveType = ArchiveType.None, MailboxFormat? mailboxFormat = null)
    {
        Name = name;
        Kind = kind;
        Extensions = extensions;
        ImageFormat = imageFormat;
        ArchiveType = archiveType;
        MailboxFormat = mailboxFormat;
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

    public static FileFormat Pst { get; } = Mailbox("PST", [".pst"], Email.Storage.MailboxFormat.Pst);

    /// <summary>Gets the offline Outlook data file format: the 4 KB-page format of the OST files of Outlook 2013 and later.</summary>
    public static FileFormat Ost { get; } = Mailbox("OST", [".ost"], Email.Storage.MailboxFormat.Pst);

    /// <summary>Gets the mbox format: an mbox file, or a folder of mbox files in the layout of Thunderbird, which is what the tool writes.</summary>
    public static FileFormat Mbox { get; } = Mailbox("Mbox", [".mbox", ".mbx"], Email.Storage.MailboxFormat.Mbox);

    /// <summary>Gets the Maildir format: a folder, whose extension only names the default output.</summary>
    public static FileFormat Maildir { get; } = Mailbox("Maildir", [".maildir"], Email.Storage.MailboxFormat.Maildir);

    /// <summary>Gets the email message format. A mailbox converted to it gives a folder of .eml files.</summary>
    public static FileFormat Eml { get; } = new("EML", FileFormatKind.EmailMessage, [".eml"], canRead: true, canWrite: true, isMultiPage: false, mailboxFormat: Email.Storage.MailboxFormat.Eml);

    /// <summary>Gets the Outlook item format. A mailbox converted to it gives a folder of .msg files.</summary>
    public static FileFormat Msg { get; } = new("MSG", FileFormatKind.EmailMessage, [".msg"], canRead: true, canWrite: true, isMultiPage: false, mailboxFormat: Email.Storage.MailboxFormat.Msg);

    public static FileFormat Oft { get; } = new("OFT", FileFormatKind.EmailMessage, [".oft"], canRead: true, canWrite: true, isMultiPage: false);

    public static FileFormat Emlx { get; } = new("EMLX", FileFormatKind.EmailMessage, [".emlx"], canRead: true, canWrite: true, isMultiPage: false);

    /// <summary>Gets the TNEF format (<c>winmail.dat</c>), which is detected from its content.</summary>
    public static FileFormat Tnef { get; } = new("TNEF", FileFormatKind.EmailMessage, [".tnef"], canRead: true, canWrite: true, isMultiPage: false);

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

    /// <summary>Gets the format a mailbox is converted to: the <see cref="FileFormatKind.Mailbox"/> formats, and the message formats a mailbox can be exported to (EML, MSG).</summary>
    public MailboxFormat? MailboxFormat { get; }

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

    private static FileFormat Mailbox(string name, string[] extensions, MailboxFormat mailboxFormat)
        => new(name, FileFormatKind.Mailbox, extensions, canRead: true, canWrite: true, isMultiPage: false, mailboxFormat: mailboxFormat);

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
        result.Add(Pst);
        result.Add(Ost);
        result.Add(Mbox);
        result.Add(Maildir);
        result.Add(Eml);
        result.Add(Msg);
        result.Add(Oft);
        result.Add(Emlx);
        result.Add(Tnef);
        return result;
    }
}
