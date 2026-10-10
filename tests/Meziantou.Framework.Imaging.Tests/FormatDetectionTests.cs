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
    [InlineData(new byte[] { 0x71, 0x6F, 0x69, 0x66, 0x00, 0x00, 0x00, 0x01 }, ImageFormat.Qoi)] // "qoif" and a width
    [InlineData(new byte[] { 0x71, 0x6F, 0x69, 0x66, 0x00, 0x00, 0x00 }, ImageFormat.Unknown)] // 7 bytes
    [InlineData(new byte[] { 0x42, 0x4D, 0x7A, 0x00, 0x00, 0x00, 0x00, 0x00 }, ImageFormat.Bmp)] // "BM" and a file size
    [InlineData(new byte[] { 0x42, 0x4E, 0x7A, 0x00, 0x00, 0x00, 0x00, 0x00 }, ImageFormat.Unknown)] // "BN"
    [InlineData(new byte[] { 0x50, 0x36, 0x0A, 0x31, 0x20, 0x31, 0x0A, 0x32 }, ImageFormat.Pnm)] // "P6" and white space
    [InlineData(new byte[] { 0x50, 0x31, 0x20, 0x31, 0x20, 0x31, 0x0A, 0x30 }, ImageFormat.Pnm)] // "P1" and a space
    [InlineData(new byte[] { 0x50, 0x37, 0x0A, 0x57, 0x49, 0x44, 0x54, 0x48 }, ImageFormat.Pnm)] // "P7" (PAM)
    [InlineData(new byte[] { 0x50, 0x38, 0x0A, 0x31, 0x20, 0x31, 0x0A, 0x32 }, ImageFormat.Unknown)] // "P8"
    [InlineData(new byte[] { 0x50, 0x36, 0x31, 0x20, 0x31, 0x0A, 0x32, 0x35 }, ImageFormat.Unknown)] // no white space after the magic
    public void DetectFormatUsesSignatures(byte[] prefix, ImageFormat expected)
        => Assert.Equal(expected, Image.DetectFormat(prefix));

    /// <summary>
    /// TGA has no signature: the whole 18-byte header must be legal and self-consistent, and the codec is consulted last
    ///.
    /// </summary>
    [Theory]
    [InlineData(2, 24, 0, 0, 0, 0, ImageFormat.Tga)] // uncompressed true color
    [InlineData(10, 32, 8, 0, 0, 0, ImageFormat.Tga)] // run-length true color with alpha
    [InlineData(3, 8, 0, 0, 0, 0, ImageFormat.Tga)] // grayscale
    [InlineData(1, 8, 0, 1, 4, 24, ImageFormat.Tga)] // color-mapped
    [InlineData(0, 24, 0, 0, 0, 0, ImageFormat.Unknown)] // image type 0 (no image data)
    [InlineData(4, 24, 0, 0, 0, 0, ImageFormat.Unknown)] // undefined image type
    [InlineData(2, 24, 0, 1, 4, 24, ImageFormat.Unknown)] // a color map with a true-color image type
    [InlineData(1, 8, 0, 0, 0, 0, ImageFormat.Unknown)] // a color-mapped image without a color map
    [InlineData(2, 23, 0, 0, 0, 0, ImageFormat.Unknown)] // undefined pixel depth
    [InlineData(2, 24, 8, 0, 0, 0, ImageFormat.Unknown)] // alpha bits a 24-bit sample cannot hold
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
}
