using System.Collections.ObjectModel;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Describes an <see cref="ImageFormat"/>: its display name, its media (MIME) types and its file extensions, and maps an
/// extension or a media type back to its format.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is a fixed table of this version of the library: an extension or a media type belongs to exactly one
/// format, so a lookup is never ambiguous. Variants of a format share the entry of their format: an animated PNG
/// (<c>.apng</c>) is <see cref="ImageFormat.Png"/>, a Netpbm bitmap (<c>.pbm</c>) is <see cref="ImageFormat.Pnm"/>, a JPEG 2000
/// codestream (<c>.j2k</c>) is <see cref="ImageFormat.Jpeg2000"/>. Use <see cref="ImageEncoder.FromFileExtension(string)"/>
/// to get the encoder an extension selects, variant included, and <see cref="ImageEncoder.Create(ImageFormat)"/> for the
/// default encoder of a format.
/// </para>
/// <para>
/// Loading never uses this table: formats are detected from content (<see cref="Image.DetectFormat(ReadOnlySpan{byte})"/>).
/// It is meant to name output files, to set a <c>Content-Type</c>, or to choose an encoder from a request.
/// </para>
/// <para>
/// The formats of the codecs an application registers (<see cref="CustomImageCodec"/>) are described by instances created
/// with the public constructor: their <see cref="Format"/> is <see cref="ImageFormat.Custom"/>, and they are not part of
/// the table (<see cref="All"/>, <see cref="Get(ImageFormat)"/>, <see cref="TryGetByFileExtension(ReadOnlySpan{char}, out ImageFormatInfo?)"/>
/// and <see cref="TryGetByMimeType(ReadOnlySpan{char}, out ImageFormatInfo?)"/> only know the built-in formats).
/// </para>
/// <para>Instances are immutable and shared; this type is thread-safe.</para>
/// </remarks>
public sealed class ImageFormatInfo
{
    private static readonly ImageFormatInfo[] Formats =
    [
        new(ImageFormat.Png, ["image/png", "image/apng", "image/vnd.mozilla.apng"], [".png", ".apng"]),
        new(ImageFormat.Gif, ["image/gif"], [".gif"]),
        new(ImageFormat.Jpeg, ["image/jpeg", "image/jpg", "image/pjpeg"], [".jpg", ".jpeg"]),
        new(ImageFormat.WebP, ["image/webp"], [".webp"]),
        new(ImageFormat.Qoi, ["image/x-qoi", "image/qoi"], [".qoi"]),
        new(ImageFormat.Bmp, ["image/bmp", "image/x-bmp", "image/x-ms-bmp"], [".bmp", ".dib"]),
        new(ImageFormat.Tga, ["image/x-tga", "image/x-targa"], [".tga", ".icb", ".vda", ".vst"]),
        new(ImageFormat.Pnm, ["image/x-portable-anymap", "image/x-portable-bitmap", "image/x-portable-graymap", "image/x-portable-greymap", "image/x-portable-pixmap", "image/x-portable-arbitrarymap"], [".pnm", ".pbm", ".pgm", ".ppm", ".pam"]),
        new(ImageFormat.Tiff, ["image/tiff"], [".tiff", ".tif"]),
        new(ImageFormat.Ico, ["image/vnd.microsoft.icon", "image/x-icon"], [".ico"]),
        new(ImageFormat.Cur, ["image/x-win-bitmap"], [".cur"]),
        new(ImageFormat.Ani, ["application/x-navi-animation"], [".ani"]),
        new(ImageFormat.Avif, ["image/avif"], [".avif", ".avifs"]),
        new(ImageFormat.JpegXl, ["image/jxl"], [".jxl"]),
        new(ImageFormat.Heic, ["image/heic", "image/heif", "image/heic-sequence", "image/heif-sequence"], [".heic", ".heif", ".heics", ".heifs"]),
        new(ImageFormat.Jpeg2000, ["image/jp2", "image/jpx", "image/jph"], [".jp2", ".j2k", ".j2c", ".jpc", ".jpx", ".jpf", ".jph", ".jhc"]),
        new(ImageFormat.OpenExr, ["image/x-exr"], [".exr"]),
    ];

    private static readonly ReadOnlyCollection<ImageFormatInfo> AllFormats = Array.AsReadOnly(Formats);

    /// <summary>Initializes the description of a format that a <see cref="CustomImageCodec"/> reads or writes.</summary>
    /// <param name="name">The display name of the format, such as <c>Farbfeld</c>.</param>
    /// <param name="mimeTypes">The media types of the format, the preferred one first: at least one, in ASCII, such as <c>image/x-farbfeld</c>. They are stored in lower case.</param>
    /// <param name="fileExtensions">
    /// The file extensions of the format, the preferred one first: at least one, in ASCII, with or without the leading period
    /// (<c>.ff</c> or <c>ff</c>). They are stored in lower case with their leading period.
    /// </param>
    /// <remarks>The <see cref="Format"/> of the description is <see cref="ImageFormat.Custom"/>.</remarks>
    /// <exception cref="ArgumentNullException">An argument or one of its items is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or white space, a list is empty, a media type is not a <c>type/subtype</c> pair of
    /// ASCII characters without white space or parameters, or an extension is empty or contains another character than an
    /// ASCII letter, digit, <c>-</c>, <c>_</c> or <c>+</c>.
    /// </exception>
    public ImageFormatInfo(string name, IEnumerable<string> mimeTypes, IEnumerable<string> fileExtensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(mimeTypes);
        ArgumentNullException.ThrowIfNull(fileExtensions);
        var types = new List<string>();
        foreach (var mimeType in mimeTypes)
        {
            ArgumentNullException.ThrowIfNull(mimeType, nameof(mimeTypes));
            types.Add(NormalizeMimeType(mimeType, nameof(mimeTypes)));
        }

        var extensions = new List<string>();
        foreach (var extension in fileExtensions)
        {
            ArgumentNullException.ThrowIfNull(extension, nameof(fileExtensions));
            extensions.Add(NormalizeFileExtension(extension, nameof(fileExtensions)));
        }

        if (types.Count == 0)
            throw new ArgumentException("A format has at least one media type.", nameof(mimeTypes));

        if (extensions.Count == 0)
            throw new ArgumentException("A format has at least one file extension.", nameof(fileExtensions));

        Format = ImageFormat.Custom;
        Name = name;
        MimeTypes = types.AsReadOnly();
        FileExtensions = extensions.AsReadOnly();
    }

    private ImageFormatInfo(ImageFormat format, string[] mimeTypes, string[] fileExtensions)
    {
        Format = format;
        Name = ImageFormatNames.Get(format);
        MimeTypes = Array.AsReadOnly(mimeTypes);
        FileExtensions = Array.AsReadOnly(fileExtensions);
    }

    /// <summary>
    /// Gets the descriptions of every built-in format, in the order of <see cref="ImageFormat"/> (<see cref="ImageFormat.Unknown"/>
    /// and <see cref="ImageFormat.Custom"/> excluded).
    /// </summary>
    public static IReadOnlyList<ImageFormatInfo> All => AllFormats;

    /// <summary>Gets the described format: <see cref="ImageFormat.Custom"/> for the format of a <see cref="CustomImageCodec"/>.</summary>
    public ImageFormat Format { get; }

    /// <summary>Gets the display name of the format, such as <c>PNG</c> or <c>JPEG XL</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets the preferred media type of the format, the first of <see cref="MimeTypes"/>: the type registered with IANA
    /// when there is one (<c>image/png</c>, <c>image/avif</c>, <c>image/jxl</c>), otherwise the type of the freedesktop.org
    /// shared MIME database (<c>image/x-tga</c>, <c>image/x-exr</c>).
    /// </summary>
    public string MimeType => MimeTypes[0];

    /// <summary>
    /// Gets the media types recognized by <see cref="TryGetByMimeType(ReadOnlySpan{char}, out ImageFormatInfo?)"/>, in
    /// lower case, the preferred one first. The others are aliases in common use (<c>image/x-icon</c>) and the types of
    /// variants of the format (<c>image/apng</c>, <c>image/heic-sequence</c>).
    /// </summary>
    public IReadOnlyList<string> MimeTypes { get; }

    /// <summary>
    /// Gets the preferred file extension of the format, the first of <see cref="FileExtensions"/>, in lower case with its
    /// leading period (<c>.png</c>, <c>.jpg</c>, <c>.tiff</c>).
    /// </summary>
    public string DefaultFileExtension => FileExtensions[0];

    /// <summary>
    /// Gets the file extensions recognized by <see cref="TryGetByFileExtension(ReadOnlySpan{char}, out ImageFormatInfo?)"/>,
    /// in lower case with their leading period, the preferred one first.
    /// </summary>
    /// <remarks>
    /// Every extension selects an encoder in <see cref="ImageEncoder.FromFileExtension(string)"/>, except the JPEG 2000
    /// extensions of files the library reads but does not write: <c>.jpx</c> and <c>.jpf</c> (the extended file format).
    /// </remarks>
    public IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Gets the description of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is <see cref="ImageFormat.Unknown"/>, <see cref="ImageFormat.Custom"/> (use
    /// <see cref="CustomImageCodec.FormatInfo"/>) or not a defined value.
    /// </exception>
    public static ImageFormatInfo Get(ImageFormat format)
    {
        foreach (var info in Formats)
        {
            if (info.Format == format)
                return info;
        }

        throw new ArgumentOutOfRangeException(nameof(format), format, "The format is unknown or not a defined value.");
    }

    /// <summary>Finds the format of a file extension.</summary>
    /// <param name="extension">
    /// The extension, with or without its leading period (<c>.jpg</c> or <c>jpg</c>), compared without regard to ASCII case.
    /// A path is not an extension: use <see cref="Path.GetExtension(ReadOnlySpan{char})"/> first.
    /// </param>
    /// <param name="info">The description of the format, or <see langword="null"/> when the extension is not recognized.</param>
    /// <returns><see langword="true"/> if the extension belongs to a supported format.</returns>
    public static bool TryGetByFileExtension(ReadOnlySpan<char> extension, [NotNullWhen(true)] out ImageFormatInfo? info)
    {
        if (extension.StartsWith('.'))
        {
            extension = extension[1..];
        }

        if (!extension.IsEmpty)
        {
            foreach (var candidate in Formats)
            {
                foreach (var known in candidate.FileExtensions)
                {
                    if (extension.Equals(known.AsSpan(1), StringComparison.OrdinalIgnoreCase))
                    {
                        info = candidate;
                        return true;
                    }
                }
            }
        }

        info = null;
        return false;
    }

    /// <summary>Finds the format of a media (MIME) type.</summary>
    /// <param name="mimeType">
    /// The media type, compared without regard to ASCII case, such as <c>image/png</c>. Parameters and surrounding white
    /// space are ignored, so the value of a <c>Content-Type</c> header can be given as is (<c>image/png; q=0.9</c>).
    /// </param>
    /// <param name="info">The description of the format, or <see langword="null"/> when the type is not recognized.</param>
    /// <returns><see langword="true"/> if the media type belongs to a supported format.</returns>
    public static bool TryGetByMimeType(ReadOnlySpan<char> mimeType, [NotNullWhen(true)] out ImageFormatInfo? info)
    {
        var separator = mimeType.IndexOf(';');
        if (separator >= 0)
        {
            mimeType = mimeType[..separator];
        }

        mimeType = mimeType.Trim();
        if (!mimeType.IsEmpty)
        {
            foreach (var candidate in Formats)
            {
                foreach (var known in candidate.MimeTypes)
                {
                    if (mimeType.Equals(known, StringComparison.OrdinalIgnoreCase))
                    {
                        info = candidate;
                        return true;
                    }
                }
            }
        }

        info = null;
        return false;
    }

    /// <summary>Determines whether a file extension is one of <see cref="FileExtensions"/>.</summary>
    /// <param name="extension">The extension, with its leading period.</param>
    /// <returns><see langword="true"/> if the extension belongs to this format, compared without regard to ASCII case.</returns>
    internal bool HasFileExtension(ReadOnlySpan<char> extension)
    {
        foreach (var known in FileExtensions)
        {
            if (extension.Equals(known, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeMimeType(string mimeType, string parameterName)
    {
        var separator = mimeType.IndexOf('/', StringComparison.Ordinal);
        var valid = separator > 0 && separator < mimeType.Length - 1 && mimeType.IndexOf('/', separator + 1, StringComparison.Ordinal) < 0;
        foreach (var c in mimeType)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('/' or '-' or '+' or '.' or '_'))
            {
                valid = false;
            }
        }

        if (!valid)
            throw new ArgumentException($"The media type '{mimeType}' is not a type/subtype pair of ASCII letters, digits, '-', '+', '.' or '_'.", parameterName);

        return ToAsciiLower(mimeType);
    }

    private static string NormalizeFileExtension(string extension, string parameterName)
    {
        var name = extension.StartsWith('.', StringComparison.Ordinal) ? extension.AsSpan(1) : extension.AsSpan();
        var valid = !name.IsEmpty;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '+'))
            {
                valid = false;
            }
        }

        if (!valid)
            throw new ArgumentException($"The file extension '{extension}' is empty or contains another character than an ASCII letter, digit, '-', '_' or '+'.", parameterName);

        return "." + ToAsciiLower(name);
    }

    private static string ToAsciiLower(ReadOnlySpan<char> value)
    {
        Span<char> buffer = value.Length <= 128 ? stackalloc char[value.Length] : new char[value.Length];
        _ = System.Text.Ascii.ToLower(value, buffer, out var written);
        return new string(buffer[..written]);
    }

    /// <summary>Returns the display name of the format.</summary>
    /// <returns>The value of <see cref="Name"/>.</returns>
    public override string ToString() => Name;
}
