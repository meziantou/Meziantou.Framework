using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.Tests;

public sealed class FormatDetectionTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ImageFormat.Png)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0x01, 0x00 }, ImageFormat.Gif)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00 }, ImageFormat.Gif)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }, ImageFormat.Jpeg)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x38, 0x61, 0x01, 0x00 }, ImageFormat.Unknown)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A }, ImageFormat.Unknown)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF }, ImageFormat.Unknown)]
    [InlineData(new byte[] { }, ImageFormat.Unknown)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }, ImageFormat.WebP)] // "RIFF" size "WEBP"
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x08, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45 }, ImageFormat.Unknown)] // a RIFF WAVE file
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42 }, ImageFormat.Unknown)] // 11 bytes
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x41, 0x43, 0x4F, 0x4E }, ImageFormat.Ani)] // "RIFF" size "ACON"
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x41, 0x43, 0x4F }, ImageFormat.Unknown)] // 11 bytes
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x58, 0x10, 0x00, 0x00, 0x00, 0x41, 0x43, 0x4F, 0x4E }, ImageFormat.Unknown)] // "RIFX" (big-endian RIFF)
    [InlineData(new byte[] { 0x71, 0x6F, 0x69, 0x66, 0x00, 0x00, 0x00, 0x01 }, ImageFormat.Qoi)] // "qoif" and a width
    [InlineData(new byte[] { 0x71, 0x6F, 0x69, 0x66, 0x00, 0x00, 0x00 }, ImageFormat.Unknown)] // 7 bytes
    [InlineData(new byte[] { 0x42, 0x4D, 0x7A, 0x00, 0x00, 0x00, 0x00, 0x00 }, ImageFormat.Bmp)] // "BM" and a file size
    [InlineData(new byte[] { 0x42, 0x4E, 0x7A, 0x00, 0x00, 0x00, 0x00, 0x00 }, ImageFormat.Unknown)] // "BN"
    [InlineData(new byte[] { 0x50, 0x36, 0x0A, 0x31, 0x20, 0x31, 0x0A, 0x32 }, ImageFormat.Pnm)] // "P6" and white space
    [InlineData(new byte[] { 0x50, 0x31, 0x20, 0x31, 0x20, 0x31, 0x0A, 0x30 }, ImageFormat.Pnm)] // "P1" and a space
    [InlineData(new byte[] { 0x50, 0x37, 0x0A, 0x57, 0x49, 0x44, 0x54, 0x48 }, ImageFormat.Pnm)] // "P7" (PAM)
    [InlineData(new byte[] { 0x50, 0x38, 0x0A, 0x31, 0x20, 0x31, 0x0A, 0x32 }, ImageFormat.Unknown)] // "P8"
    [InlineData(new byte[] { 0x50, 0x36, 0x31, 0x20, 0x31, 0x0A, 0x32, 0x35 }, ImageFormat.Unknown)] // no white space after the magic
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 }, ImageFormat.Avif)] // box size, "ftyp", major brand "avif"
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x73 }, ImageFormat.Avif)] // "avis" (image sequence)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x69, 0x66, 0x31 }, ImageFormat.Avif)] // "mif1" (HEIF image)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x73, 0x66, 0x31 }, ImageFormat.Avif)] // "msf1" (HEIF sequence)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x69, 0x61, 0x66 }, ImageFormat.Avif)] // "miaf"
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x41, 0x31, 0x42 }, ImageFormat.Avif)] // "MA1B" (AVIF baseline profile)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x41, 0x31, 0x41 }, ImageFormat.Avif)] // "MA1A" (AVIF advanced profile)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69 }, ImageFormat.Unknown)] // 11 bytes: the major brand is incomplete
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63 }, ImageFormat.Heic)] // "heic" (HEVC-coded HEIF)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x78 }, ImageFormat.Heic)] // "heix" (HEVC beyond the Main profile)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x76, 0x63 }, ImageFormat.Heic)] // "hevc" (HEVC image sequence)
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x76, 0x78 }, ImageFormat.Heic)] // "hevx"
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x6D }, ImageFormat.Heic)] // "heim"
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x73 }, ImageFormat.Heic)] // "heis"
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x76, 0x6D }, ImageFormat.Heic)] // "hevm"
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x76, 0x73 }, ImageFormat.Heic)] // "hevs"
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x66 }, ImageFormat.Unknown)] // "heif" is not a brand
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69 }, ImageFormat.Unknown)] // 11 bytes: the major brand is incomplete
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x6D, 0x6F, 0x6F, 0x76, 0x61, 0x76, 0x69, 0x66 }, ImageFormat.Unknown)] // "moov" instead of "ftyp"
    [InlineData(new byte[] { 0xFF, 0x0A, 0x10, 0x50, 0x10, 0x50, 0x5C, 0x08 }, ImageFormat.JpegXl)] // a JPEG XL codestream
    [InlineData(new byte[] { 0xFF, 0x0B, 0x10, 0x50, 0x10, 0x50, 0x5C, 0x08 }, ImageFormat.Unknown)] // not the second byte of a codestream
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A }, ImageFormat.JpegXl)] // the signature box of a JPEG XL container
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87 }, ImageFormat.Unknown)] // 11 bytes
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0B }, ImageFormat.Unknown)] // the last byte of the signature differs
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A }, ImageFormat.Jpeg2000)] // the signature box of the JP2 family of files
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87 }, ImageFormat.Unknown)] // 11 bytes
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0B }, ImageFormat.Unknown)] // the last byte of the signature differs
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x32, 0x20, 0x0D, 0x0A, 0x87, 0x0A }, ImageFormat.Unknown)] // "jP2 " is not the type of the signature box
    [InlineData(new byte[] { 0xFF, 0x4F, 0xFF, 0x51, 0x00, 0x29, 0x00, 0x00 }, ImageFormat.Jpeg2000)] // a JPEG 2000 codestream: the SOC and SIZ markers
    [InlineData(new byte[] { 0xFF, 0x4F, 0xFF, 0x52, 0x00, 0x0C, 0x00, 0x00 }, ImageFormat.Unknown)] // the SIZ marker segment is the first one
    [InlineData(new byte[] { 0xFF, 0x4E, 0xFF, 0x51, 0x00, 0x29, 0x00, 0x00 }, ImageFormat.Unknown)] // not the SOC marker
    public void DetectFormatUsesSignatures(byte[] prefix, ImageFormat expected)
        => Assert.Equal(expected, Image.DetectFormat(prefix));

    /// <summary>
    /// A generic HEIF major brand does not tell the codec of the images: the compatible brands do, when the prefix holds the
    /// whole file type box. A box that the prefix cuts is reported as AVIF, and the file is read as what its brands declare.
    /// </summary>
    [Theory]
    [InlineData("mif1", new[] { "mif1", "avif" }, ImageFormat.Avif)]
    [InlineData("mif1", new[] { "mif1", "heic" }, ImageFormat.Heic)]
    [InlineData("mif1", new[] { "heix", "miaf" }, ImageFormat.Heic)]
    [InlineData("msf1", new[] { "msf1", "hevc" }, ImageFormat.Heic)]
    [InlineData("msf1", new[] { "msf1", "avis" }, ImageFormat.Avif)]
    [InlineData("miaf", new[] { "heic", "avif" }, ImageFormat.Avif)] // both: the codec of the images decides when the file is read
    [InlineData("mif1", new[] { "mif1", "miaf" }, ImageFormat.Unknown)] // no brand of a codec that the library reads
    [InlineData("mif1", new string[0], ImageFormat.Unknown)]
    [InlineData("avif", new[] { "heic" }, ImageFormat.Avif)] // the major brand of a codec is enough
    [InlineData("heic", new[] { "avif" }, ImageFormat.Heic)]
    public void GenericHeifBrandsAreDetectedFromTheCompatibleBrands(string majorBrand, string[] compatibleBrands, ImageFormat expected)
    {
        var box = new byte[16 + (4 * compatibleBrands.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes("ftyp" + majorBrand).CopyTo(box, 4);
        for (var i = 0; i < compatibleBrands.Length; i++)
        {
            Encoding.ASCII.GetBytes(compatibleBrands[i]).CopyTo(box, 16 + (4 * i));
        }

        Assert.Equal(expected, Image.DetectFormat(box));

        // What follows the box does not matter, and a box that is cut cannot tell
        Assert.Equal(expected, Image.DetectFormat([.. box, .. new byte[8]]));
        if (compatibleBrands.Length > 0 && majorBrand is "mif1" or "msf1" or "miaf")
            Assert.Equal(ImageFormat.Avif, Image.DetectFormat(box.AsSpan(0, box.Length - 1)));
    }

    /// <summary>
    /// A file type box whose size is in the 64-bit field has its major brand at offset 16: the detection prefix holds its
    /// first two characters, which decide however many bytes follow, so that a reader that detects the format from the
    /// prefix only agrees with a detection that sees the whole file.
    /// </summary>
    [Theory]
    [InlineData("avif", ImageFormat.Avif)]
    [InlineData("mif1", ImageFormat.Avif)]
    [InlineData("msf1", ImageFormat.Avif)]
    [InlineData("MA1B", ImageFormat.Avif)]
    [InlineData("mi\0\0", ImageFormat.Avif)] // not a brand: the reader rejects the file type box
    [InlineData("heic", ImageFormat.Heic)]
    [InlineData("hevc", ImageFormat.Heic)]
    [InlineData("he\0\0", ImageFormat.Heic)]
    [InlineData("jpeg", ImageFormat.Unknown)]
    public void LargeSizeFileTypeBoxIsDetectedFromTheDetectionPrefix(string majorBrand, ImageFormat expected)
    {
        var box = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes("ftyp").CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        Encoding.ASCII.GetBytes(majorBrand).CopyTo(box, 16);
        Encoding.ASCII.GetBytes("mif1").CopyTo(box, 24);

        for (var length = Image.FormatDetectionPrefixLength; length <= box.Length; length++)
        {
            Assert.Equal(expected, Image.DetectFormat(box.AsSpan(0, length)));
        }
    }

    /// <summary>
    /// TGA has no signature: the whole 18-byte header must be legal and self-consistent, and the codec is consulted last
    ///.
    /// </summary>
    [Theory]
    [InlineData(2, 24, 0, 0, 0, 0, ImageFormat.Tga)] // uncompressed true color
    [InlineData(10, 32, 8, 0, 0, 0, ImageFormat.Tga)] // run-length true color with alpha
    [InlineData(3, 8, 0, 0, 0, 0, ImageFormat.Tga)] // grayscale
    [InlineData(1, 8, 0, 1, 4, 24, ImageFormat.Tga)] // color-mapped
    [InlineData(9, 16, 0, 1, 4, 24, ImageFormat.Tga)] // run-length color-mapped with 16-bit indexes
    [InlineData(3, 16, 8, 0, 0, 0, ImageFormat.Tga)] // grayscale with an alpha attribute
    [InlineData(11, 16, 0, 0, 0, 0, ImageFormat.Tga)] // run-length grayscale with an unspecified attribute
    [InlineData(0, 24, 0, 0, 0, 0, ImageFormat.Unknown)] // image type 0 (no image data)
    [InlineData(4, 24, 0, 0, 0, 0, ImageFormat.Unknown)] // undefined image type
    [InlineData(2, 24, 0, 1, 4, 24, ImageFormat.Unknown)] // a color map with a true-color image type
    [InlineData(1, 8, 0, 0, 0, 0, ImageFormat.Unknown)] // a color-mapped image without a color map
    [InlineData(2, 23, 0, 0, 0, 0, ImageFormat.Unknown)] // undefined pixel depth
    [InlineData(2, 24, 8, 0, 0, 0, ImageFormat.Unknown)] // alpha bits a 24-bit sample cannot hold
    [InlineData(3, 8, 8, 0, 0, 0, ImageFormat.Unknown)] // alpha bits an 8-bit gray pixel cannot hold
    [InlineData(3, 16, 1, 0, 0, 0, ImageFormat.Unknown)] // the attribute of a 16-bit gray pixel is a whole byte
    [InlineData(3, 24, 0, 0, 0, 0, ImageFormat.Unknown)] // undefined grayscale depth
    [InlineData(1, 24, 0, 1, 4, 24, ImageFormat.Unknown)] // color-map indexes wider than 16 bits
    public void TgaIsDetectedFromAPlausibleHeader(int imageType, int pixelDepth, int alphaBits, int colorMapType, int colorMapLength, int colorMapEntryBits, ImageFormat expected)
    {
        var header = new byte[18];
        header[1] = (byte)colorMapType;
        header[2] = (byte)imageType;
        header[5] = (byte)colorMapLength;
        header[7] = (byte)colorMapEntryBits;
        header[12] = 4; // width
        header[14] = 4; // height
        header[16] = (byte)pixelDepth;
        header[17] = (byte)alphaBits;
        Assert.Equal(expected, Image.DetectFormat(header));
    }

    [Fact]
    public void TgaNeedsItsWholeHeaderToBeRecognized()
    {
        var header = new byte[18];
        header[2] = 2;
        header[12] = 4;
        header[14] = 4;
        header[16] = 24;
        Assert.Equal(ImageFormat.Tga, Image.DetectFormat(header));
        Assert.Equal(ImageFormat.Unknown, Image.DetectFormat(header.AsSpan(0, 17)));
        Assert.Equal(18, Image.FormatDetectionPrefixLength);
    }

    [Theory]
    [InlineData(ImageFormat.Png, "PNG", "image/png", ".png")]
    [InlineData(ImageFormat.Gif, "GIF", "image/gif", ".gif")]
    [InlineData(ImageFormat.Jpeg, "JPEG", "image/jpeg", ".jpg")]
    [InlineData(ImageFormat.WebP, "WebP", "image/webp", ".webp")]
    [InlineData(ImageFormat.Qoi, "QOI", "image/x-qoi", ".qoi")]
    [InlineData(ImageFormat.Bmp, "BMP", "image/bmp", ".bmp")]
    [InlineData(ImageFormat.Tga, "TGA", "image/x-tga", ".tga")]
    [InlineData(ImageFormat.Pnm, "PNM", "image/x-portable-anymap", ".pnm")]
    [InlineData(ImageFormat.Tiff, "TIFF", "image/tiff", ".tiff")]
    [InlineData(ImageFormat.Ico, "ICO", "image/vnd.microsoft.icon", ".ico")]
    [InlineData(ImageFormat.Cur, "CUR", "image/x-win-bitmap", ".cur")]
    [InlineData(ImageFormat.Ani, "ANI", "application/x-navi-animation", ".ani")]
    [InlineData(ImageFormat.Avif, "AVIF", "image/avif", ".avif")]
    [InlineData(ImageFormat.JpegXl, "JPEG XL", "image/jxl", ".jxl")]
    [InlineData(ImageFormat.Heic, "HEIC", "image/heic", ".heic")]
    [InlineData(ImageFormat.Jpeg2000, "JPEG 2000", "image/jp2", ".jp2")]
    [InlineData(ImageFormat.OpenExr, "OpenEXR", "image/x-exr", ".exr")]
    public void FormatsDescribeTheirNameMimeTypeAndExtension(ImageFormat format, string name, string mimeType, string extension)
    {
        // The registered media types (IANA) where there is one, otherwise those of the freedesktop.org shared MIME
        // database, which libmagic and Python's mimetypes report too
        var info = ImageFormatInfo.Get(format);
        Assert.Equal(format, info.Format);
        Assert.Equal(name, info.Name);
        Assert.Equal(name, info.ToString());
        Assert.Equal(mimeType, info.MimeType);
        Assert.Equal(mimeType, info.MimeTypes[0]);
        Assert.Equal(extension, info.DefaultFileExtension);
        Assert.Equal(extension, info.FileExtensions[0]);
        Assert.Same(info, ImageFormatInfo.Get(format));

        Assert.True(ImageFormatInfo.TryGetByMimeType(mimeType, out var byMimeType));
        Assert.Same(info, byMimeType);
        Assert.True(ImageFormatInfo.TryGetByFileExtension(extension, out var byExtension));
        Assert.Same(info, byExtension);
    }

    [Fact]
    public void EveryFormatIsDescribedOnceAndEveryNameMapsToOneFormat()
    {
        var formats = Enum.GetValues<ImageFormat>().Where(format => format != ImageFormat.Unknown).ToArray();
        Assert.Equal(formats, ImageFormatInfo.All.Select(info => info.Format));

        var extensions = ImageFormatInfo.All.SelectMany(info => info.FileExtensions).ToArray();
        var mimeTypes = ImageFormatInfo.All.SelectMany(info => info.MimeTypes).ToArray();
        Assert.HasCount(extensions.Length, extensions.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.HasCount(mimeTypes.Length, mimeTypes.Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var info in ImageFormatInfo.All)
        {
            Assert.NotEmpty(info.MimeTypes);
            Assert.NotEmpty(info.FileExtensions);
            foreach (var extension in info.FileExtensions)
            {
                Assert.StartsWith(".", extension, StringComparison.Ordinal);
                Assert.Equal(extension.ToLowerInvariant(), extension);
                Assert.True(ImageFormatInfo.TryGetByFileExtension(extension, out var found));
                Assert.Same(info, found);
            }

            foreach (var mimeType in info.MimeTypes)
            {
                Assert.Equal(mimeType.ToLowerInvariant(), mimeType);
                Assert.True(ImageFormatInfo.TryGetByMimeType(mimeType, out var found));
                Assert.Same(info, found);
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>("format", () => ImageFormatInfo.Get(ImageFormat.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>("format", () => ImageFormatInfo.Get((ImageFormat)1000));
    }

    [Theory]
    [InlineData("jpg", ImageFormat.Jpeg)]
    [InlineData(".JPEG", ImageFormat.Jpeg)]
    [InlineData("apng", ImageFormat.Png)]
    [InlineData(".Pbm", ImageFormat.Pnm)]
    [InlineData(".tif", ImageFormat.Tiff)]
    [InlineData(".cur", ImageFormat.Cur)]
    [InlineData(".avifs", ImageFormat.Avif)]
    [InlineData(".heifs", ImageFormat.Heic)]
    [InlineData(".j2k", ImageFormat.Jpeg2000)]
    [InlineData(".jph", ImageFormat.Jpeg2000)]
    [InlineData("", ImageFormat.Unknown)]
    [InlineData(".", ImageFormat.Unknown)]
    [InlineData("..png", ImageFormat.Unknown)]
    [InlineData("photo.png", ImageFormat.Unknown)]
    [InlineData(" .png", ImageFormat.Unknown)]
    [InlineData(".xyz", ImageFormat.Unknown)]
    public void FormatsAreFoundByFileExtension(string extension, ImageFormat expected)
    {
        var found = ImageFormatInfo.TryGetByFileExtension(extension, out var info);
        Assert.Equal(expected != ImageFormat.Unknown, found);
        Assert.Equal(expected, info?.Format ?? ImageFormat.Unknown);
    }

    [Theory]
    [InlineData("image/png", ImageFormat.Png)]
    [InlineData("IMAGE/PNG", ImageFormat.Png)]
    [InlineData("image/apng", ImageFormat.Png)]
    [InlineData("  image/jpeg ; q=0.9", ImageFormat.Jpeg)]
    [InlineData("image/jpeg;charset=binary", ImageFormat.Jpeg)]
    [InlineData("image/x-icon", ImageFormat.Ico)]
    [InlineData("image/heif-sequence", ImageFormat.Heic)]
    [InlineData("image/x-portable-pixmap", ImageFormat.Pnm)]
    [InlineData("image/svg+xml", ImageFormat.Unknown)]
    [InlineData("image/*", ImageFormat.Unknown)]
    [InlineData("image", ImageFormat.Unknown)]
    [InlineData("png", ImageFormat.Unknown)]
    [InlineData("", ImageFormat.Unknown)]
    [InlineData(" ; ", ImageFormat.Unknown)]
    public void FormatsAreFoundByMimeType(string mimeType, ImageFormat expected)
    {
        var found = ImageFormatInfo.TryGetByMimeType(mimeType, out var info);
        Assert.Equal(expected != ImageFormat.Unknown, found);
        Assert.Equal(expected, info?.Format ?? ImageFormat.Unknown);
    }
}
