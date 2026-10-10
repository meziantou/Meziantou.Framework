using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>Canonical text descriptions of <see cref="ImageInfo"/> values (every field and metadata payload hash), used to compare results across input variants.</summary>
public static class ImageInfoSnapshots
{
    /// <summary>Describes every field of the information, including metadata payload hashes and text entries.</summary>
    public static string Describe(ImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{info.Format} {info.Width}x{info.Height} {info.PixelFormat} {info.ColorModel} {info.BitsPerComponent}bpc");
        builder.Append(CultureInfo.InvariantCulture, $" frames={Format(info.FrameCount)} animated={Format(info.IsAnimated)} poster={Format(info.HasPosterFrame)} transparency={Format(info.MayHaveTransparency)}");
        builder.Append(CultureInfo.InvariantCulture, $" plays={(info.Animation is null ? "none" : Format(info.Animation.TotalPlays))} mode={info.IdentifyMode}");
        builder.Append(" | ").Append(DescribeMetadata(info.Metadata));
        return builder.ToString();
    }

    /// <summary>Describes metadata: source format, orientation, resolution, payload hashes and text entries.</summary>
    public static string DescribeMetadata(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"source={metadata.SourceFormat} orientation={metadata.Orientation}");
        builder.Append(CultureInfo.InvariantCulture, $" resolution={(metadata.Resolution is null ? "none" : metadata.Resolution.ToString())}");
        builder.Append(CultureInfo.InvariantCulture, $" transfer={metadata.TransferFunction}");
        builder.Append(" icc=").Append(Hash(metadata.IccProfile?.Data));
        builder.Append(" exif=").Append(Hash(metadata.ExifProfile?.Data));
        builder.Append(" xmp=").Append(Hash(metadata.XmpProfile?.Data));
        foreach (var entry in metadata.TextEntries)
        {
            builder.Append(CultureInfo.InvariantCulture, $" text[{entry.Keyword}|{entry.LanguageTag}|{entry.TranslatedKeyword}]={entry.Value}");
        }

        return builder.ToString();
    }

    private static string Hash(MetadataBlob? blob) => blob is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{blob.Length}:{Convert.ToHexStringLower(SHA256.HashData(blob.Span))}");

    private static string Format<T>(T? value)
        where T : struct
        => value is null ? "?" : Convert.ToString(value.Value, CultureInfo.InvariantCulture)!;
}
