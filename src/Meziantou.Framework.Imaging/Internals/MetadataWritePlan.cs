using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The metadata an encoder writes, computed once from <see cref="ImageMetadata"/>, the output format and the encoder's
/// <see cref="MetadataHandling"/> policy. Every encoder uses this plan so that the save-time
/// metadata policy is identical across formats:
/// <list type="bullet">
/// <item><see cref="MetadataHandling.Strict"/>: metadata the format cannot represent throws <see cref="UnsupportedImageFeatureException"/>;</item>
/// <item><see cref="MetadataHandling.DiscardUnsupported"/>: such metadata is silently dropped;</item>
/// <item><see cref="MetadataHandling.Strip"/>: no optional metadata is written (and nothing is validated).</item>
/// </list>
/// Malformed caller-supplied payloads (EXIF, ICC, XMP) that would be written throw <see cref="InvalidImageContentException"/>
/// regardless of the policy (except <see cref="MetadataHandling.Strip"/>). The typed orientation is authoritative: the EXIF
/// payload is rewritten (or synthesized) from it and its pixel-dimension tags are reconciled with the canvas size.
/// <see cref="ImageMetadata.SourceFormat"/> never influences the plan. Animation timing is not metadata and is not handled here.
/// </summary>
[SuppressMessage("Design", "MA0182:Unused internal type", Justification = "Shared codec infrastructure; consumed by the codecs and covered by unit tests.")]
internal sealed class MetadataWritePlan
{
    /// <summary>The largest XMP packet storable in one JPEG APP1 segment (65,533 payload bytes minus the 29-byte namespace header).</summary>
    public const int MaxJpegStandardXmpBytes = 65_533 - 29;

    /// <summary>The largest EXIF payload storable in one JPEG APP1 segment (65,533 payload bytes minus the 6-byte <c>Exif\0\0</c> header).</summary>
    public const int MaxJpegExifBytes = 65_533 - 6;

    /// <summary>The largest ICC profile storable in JPEG APP2 segments (255 segments of 65,533 - 14 bytes).</summary>
    public const long MaxJpegIccBytes = 255L * (65_533 - 14);

    /// <summary>The largest comment storable in one JPEG COM segment (Latin-1, one byte per character).</summary>
    public const int MaxJpegCommentLength = 65_533;

    private static readonly MetadataWritePlan Empty = new(null, null, null, [], null, ColorTransferFunction.Srgb);

    private MetadataWritePlan(IccProfile? iccProfile, byte[]? exif, XmpProfile? xmpProfile, IReadOnlyList<ImageTextEntry> textEntries, ImageResolution? resolution, ColorTransferFunction transferFunction, ExifOrientation orientation = ExifOrientation.TopLeft)
    {
        TransferFunction = transferFunction;
        IccProfile = iccProfile;
        Exif = exif;
        XmpProfile = xmpProfile;
        TextEntries = textEntries;
        Resolution = resolution;
        Orientation = orientation;
    }

    /// <summary>
    /// Gets the orientation to write in the format's own orientation field (the TIFF <c>Orientation</c> tag), or
    /// <see cref="ExifOrientation.TopLeft"/>. Formats that store orientation inside an EXIF block get it from
    /// <see cref="Exif"/> instead, which <see cref="ExifTiff"/> already reconciled.
    /// </summary>
    public ExifOrientation Orientation { get; }

    /// <summary>Gets the ICC profile to write, or <see langword="null"/>.</summary>
    public IccProfile? IccProfile { get; }

    /// <summary>Gets the TIFF-structured EXIF data to write (orientation and dimensions reconciled), or <see langword="null"/>.</summary>
    public byte[]? Exif { get; }

    /// <summary>Gets the XMP packet to write, or <see langword="null"/>.</summary>
    public XmpProfile? XmpProfile { get; }

    /// <summary>Gets the text entries to write, in order.</summary>
    public IReadOnlyList<ImageTextEntry> TextEntries { get; }

    /// <summary>Gets the resolution to write, or <see langword="null"/>.</summary>
    public ImageResolution? Resolution { get; }

    /// <summary>
    /// Gets the transfer function to declare: <see cref="ColorTransferFunction.Linear"/> only for formats that store the label
    /// (QOI); <see cref="ColorTransferFunction.Srgb"/> otherwise, or when the label is discarded or stripped.
    /// </summary>
    public ColorTransferFunction TransferFunction { get; }

    /// <summary>Creates the plan for an encoder.</summary>
    /// <param name="metadata">The image metadata, or <see langword="null"/> for none.</param>
    /// <param name="format">The output format.</param>
    /// <param name="handling">The encoder's metadata policy.</param>
    /// <param name="canvasSize">The canvas size, used to reconcile EXIF dimension tags.</param>
    /// <param name="pixelFormat">
    /// The pixel format of the written frames, or <see langword="null"/> when unknown. When known, an ICC profile whose color
    /// space cannot label these pixels (gray profiles for gray formats, RGB profiles for color
    /// formats) is unsupported metadata: it follows <paramref name="handling"/> like any other unrepresentable item.
    /// </param>
    /// <returns>The plan.</returns>
    /// <exception cref="UnsupportedImageFeatureException">Metadata cannot be represented and <paramref name="handling"/> is <see cref="MetadataHandling.Strict"/>.</exception>
    /// <exception cref="InvalidImageContentException">A payload that would be written is malformed.</exception>
    public static MetadataWritePlan Create(ImageMetadata? metadata, ImageFormat format, MetadataHandling handling, Size canvasSize, PixelFormat? pixelFormat = null)
    {
        if (metadata is null || handling == MetadataHandling.Strip)
            return Empty;

        var capabilities = FormatCapabilities.Get(format);
        var context = new PlanContext(format, handling);

        IccProfile? icc = null;
        if (metadata.IccProfile is { } iccProfile)
        {
            if (!capabilities.Icc)
            {
                context.Unsupported("ICC color profile");
            }
            else
            {
                if (!MetadataValidation.TryValidateIccProfile(iccProfile.Data.Span, out var error))
                    throw new InvalidImageContentException("The ICC profile is malformed: " + error, format);

                // WebP stores RGB samples whatever the pixel format: a gray source is written as RGB
                var labeled = format == ImageFormat.WebP && pixelFormat is { } source && PixelFormats.IsGrayscale(source) ? PixelFormat.Rgb24 : pixelFormat;
                if (labeled is { } pixels && !ColorProfileCompatibility.IsCompatible(iccProfile.ColorSpace, pixels))
                {
                    context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"ICC color profile declaring the {iccProfile.ColorSpace} color space for {pixels} pixels"));
                }
                else if (format == ImageFormat.Jpeg && iccProfile.Data.Length > MaxJpegIccBytes)
                {
                    context.Unsupported("ICC color profile larger than 255 JPEG APP2 segments");
                }
                else
                {
                    icc = iccProfile;
                }
            }
        }

        byte[]? exif = null;
        if (capabilities.Exif)
        {
            if (metadata.ExifProfile is { } exifProfile)
            {
                exif = ExifTiff.Rewrite(exifProfile.Data.Span, metadata.Orientation, canvasSize, removeThumbnail: false, format);
            }
            else if (metadata.Orientation != ExifOrientation.TopLeft)
            {
                exif = ExifTiff.CreateMinimal(metadata.Orientation);
            }

            if (exif is not null && format == ImageFormat.Jpeg && exif.Length > MaxJpegExifBytes)
            {
                context.Unsupported("EXIF profile larger than one JPEG APP1 segment");
                exif = null;
            }
        }
        else
        {
            if (metadata.ExifProfile is not null)
            {
                context.Unsupported("EXIF profile");
            }

            if (metadata.Orientation != ExifOrientation.TopLeft && !capabilities.NativeOrientation)
            {
                context.Unsupported("EXIF orientation other than TopLeft");
            }
        }

        XmpProfile? xmp = null;
        if (metadata.XmpProfile is { } xmpProfile)
        {
            if (!capabilities.Xmp)
            {
                context.Unsupported("XMP packet");
            }
            else
            {
                if (!MetadataValidation.TryValidateXmpPacket(xmpProfile.Data.Span, out var error))
                    throw new InvalidImageContentException("The XMP packet is malformed: " + error, format);

                if (format == ImageFormat.Jpeg && xmpProfile.Data.Length > MaxJpegStandardXmpBytes)
                {
                    context.Unsupported("XMP packet larger than one JPEG APP1 segment (extended XMP)");
                }
                else
                {
                    xmp = xmpProfile;
                }
            }
        }

        var text = new List<ImageTextEntry>(metadata.TextEntries.Count);
        foreach (var entry in metadata.TextEntries)
        {
            if (capabilities.IsTextEntrySupported(entry))
            {
                text.Add(entry);
            }
            else
            {
                context.Unsupported(string.Create(CultureInfo.InvariantCulture, $"text entry '{entry.Keyword}'"));
            }
        }

        ImageResolution? resolution = null;
        if (metadata.Resolution is { } value)
        {
            if (!capabilities.Resolution)
            {
                context.Unsupported("resolution");
            }
            else
            {
                bool representable;
                try
                {
                    if (format == ImageFormat.Jpeg)
                    {
                        _ = ResolutionConversion.ToJfifDensity(value);
                    }
                    else if (format == ImageFormat.Tiff)
                    {
                        _ = ResolutionConversion.ToTiffRational(value);
                    }
                    else
                    {
                        _ = ResolutionConversion.ToPngPhys(value);
                    }

                    representable = true;
                }
                catch (UnsupportedImageFeatureException)
                {
                    representable = false;
                }

                if (representable)
                {
                    resolution = value;
                }
                else
                {
                    context.Unsupported("resolution (outside the range of the format's integer density fields)");
                }
            }
        }

        var transferFunction = ColorTransferFunction.Srgb;
        if (metadata.TransferFunction == ColorTransferFunction.Linear)
        {
            if (capabilities.LinearTransfer)
            {
                transferFunction = ColorTransferFunction.Linear;
            }
            else
            {
                // Writing linear-light samples without the label would relabel them as sRGB
                context.Unsupported("linear transfer function (linear-light samples would be read as sRGB)");
            }
        }

        var orientation = capabilities.NativeOrientation ? metadata.Orientation : ExifOrientation.TopLeft;
        return new MetadataWritePlan(icc, exif, xmp, text, resolution, transferFunction, orientation);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct PlanContext(ImageFormat format, MetadataHandling handling)
    {
        public void Unsupported(string feature)
        {
            if (handling == MetadataHandling.Strict)
                throw new UnsupportedImageFeatureException($"The {format} format cannot store the {feature}. Remove it, or set the encoder's MetadataHandling to DiscardUnsupported or Strip.", format, "Metadata: " + feature);
        }
    }

    /// <summary>The metadata each output format can store.</summary>
    internal sealed class FormatCapabilities
    {
        private static readonly FormatCapabilities None = new(icc: false, exif: false, xmp: false, resolution: false, TextSupport.None);
        private static readonly FormatCapabilities Qoi = new(icc: false, exif: false, xmp: false, resolution: false, TextSupport.None, linearTransfer: true);
        private static readonly FormatCapabilities Png = new(icc: true, exif: true, xmp: true, resolution: true, TextSupport.PngKeywords);
        private static readonly FormatCapabilities Gif = new(icc: false, exif: false, xmp: false, resolution: false, TextSupport.CommentsOnly);
        private static readonly FormatCapabilities Jpeg = new(icc: true, exif: true, xmp: true, resolution: true, TextSupport.JpegComments);
        private static readonly FormatCapabilities WebP = new(icc: true, exif: true, xmp: true, resolution: false, TextSupport.None);
        private static readonly FormatCapabilities Bmp = new(icc: false, exif: false, xmp: false, resolution: true, TextSupport.None);

        // TIFF stores the orientation in its own tag, so no EXIF block has to be synthesized for it
        private static readonly FormatCapabilities Tiff = new(icc: true, exif: false, xmp: true, resolution: true, TextSupport.None, nativeOrientation: true);

        private FormatCapabilities(bool icc, bool exif, bool xmp, bool resolution, TextSupport text, bool linearTransfer = false, bool nativeOrientation = false)
        {
            LinearTransfer = linearTransfer;
            NativeOrientation = nativeOrientation;
            Icc = icc;
            Exif = exif;
            Xmp = xmp;
            Resolution = resolution;
            Text = text;
        }

        private enum TextSupport
        {
            None,
            CommentsOnly,
            JpegComments,
            PngKeywords,
        }

        public bool Icc { get; }

        public bool Exif { get; }

        public bool Xmp { get; }

        public bool Resolution { get; }

        /// <summary>Gets a value indicating whether the format can declare linear-light samples (QOI colorspace 1).</summary>
        public bool LinearTransfer { get; }

        /// <summary>Gets a value indicating whether the format stores the orientation in a field of its own (the TIFF <c>Orientation</c> tag).</summary>
        public bool NativeOrientation { get; }

        private TextSupport Text { get; }

        public static FormatCapabilities Get(ImageFormat format) => format switch
        {
            ImageFormat.Png => Png,
            ImageFormat.Gif => Gif,
            ImageFormat.Jpeg => Jpeg,
            ImageFormat.WebP => WebP,
            ImageFormat.Qoi => Qoi,
            ImageFormat.Bmp => Bmp,
            ImageFormat.Tiff => Tiff,
            _ => None,
        };

        public bool IsTextEntrySupported(ImageTextEntry entry) => Text switch
        {
            // GIF comment extensions store a bare comment (no keyword, language or translation) in any number of sub-blocks;
            // they are read as Latin-1, so the value must be Latin-1 to round-trip
            TextSupport.CommentsOnly => IsBareComment(entry) && !entry.Value.AsSpan().ContainsAnyExceptInRange('\0', 'ÿ'),

            // JPEG COM segments are read as Latin-1: one segment per comment, so the value must be
            // Latin-1 and fit one segment (a longer comment would come back as several entries)
            TextSupport.JpegComments => IsBareComment(entry) && entry.Value.Length <= MaxJpegCommentLength && !entry.Value.AsSpan().ContainsAnyExceptInRange('\0', 'ÿ'),
            TextSupport.PngKeywords => IsValidPngTextEntry(entry),
            _ => false,
        };

        private static bool IsBareComment(ImageTextEntry entry) => string.Equals(entry.Keyword, ImageTextEntry.CommentKeyword, StringComparison.Ordinal) && entry.LanguageTag is null && entry.TranslatedKeyword is null;

        /// <summary>
        /// PNG text entries (tEXt/zTXt/iTXt): a valid keyword (<see cref="IsValidPngKeyword"/>) other than the XMP keyword
        /// <c>XML:com.adobe.xmp</c> (reserved for <see cref="ImageMetadata.XmpProfile"/>); a value and a translated keyword
        /// without NUL characters and encodable as UTF-8 (no unpaired surrogate); a language tag made of ASCII letters, digits
        /// and hyphens (RFC 5646 syntax characters).
        /// </summary>
        internal static bool IsValidPngTextEntry(ImageTextEntry entry)
        {
            if (!IsValidPngKeyword(entry.Keyword) || string.Equals(entry.Keyword, "XML:com.adobe.xmp", StringComparison.Ordinal))
                return false;

            if (!IsValidPngText(entry.Value) || (entry.TranslatedKeyword is not null && !IsValidPngText(entry.TranslatedKeyword)))
                return false;

            if (entry.LanguageTag is { } language)
            {
                foreach (var c in language)
                {
                    if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                        return false;
                }
            }

            return true;

            static bool IsValidPngText(string value) => !value.Contains('\0', StringComparison.Ordinal) && !HasLoneSurrogate(value);

            static bool HasLoneSurrogate(string value)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        i++;
                    }
                    else if (char.IsSurrogate(value[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>PNG keywords: 1-79 printable Latin-1 characters (32-126, 161-255), no leading, trailing or consecutive spaces.</summary>
        internal static bool IsValidPngKeyword(string keyword)
        {
            if (keyword.Length is < 1 or > 79 || keyword[0] == ' ' || keyword[^1] == ' ')
                return false;

            for (var i = 0; i < keyword.Length; i++)
            {
                var c = keyword[i];
                if (c is not ((>= ' ' and <= '~') or (>= '¡' and <= 'ÿ')))
                    return false;

                if (c == ' ' && keyword[i - 1] == ' ')
                    return false;
            }

            return true;
        }
    }
}
