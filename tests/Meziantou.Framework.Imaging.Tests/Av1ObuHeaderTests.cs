using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The OBU header (AV1 specification, sections 5.3.1 to 5.3.3): forbidden bit (1), obu_type (4), obu_extension_flag (1),
/// obu_has_size_field (1), reserved (1); the optional extension byte: temporal_id (3), spatial_id (2), reserved (3); the
/// optional leb128 obu_size. The headers are assembled by hand from that layout.
/// </summary>
public sealed class Av1ObuHeaderTests
{
    [Fact]
    public void HeaderWithSizeField()
    {
        // 0 0001 0 1 0: a sequence header (1) with a size field; obu_size = 3; one byte follows the unit
        var header = Av1ObuHeader.Read([0b0000_1010, 0x03, 0xAA, 0xBB, 0xCC, 0xFF]);
        Assert.Equal(new Av1ObuHeader(Type: 1, HasExtension: false, TemporalId: 0, SpatialId: 0, HeaderLength: 2, PayloadLength: 3), header);
        Assert.Equal(5, header.TotalLength);

        // An empty payload at the very end of the data: a temporal delimiter (2)
        Assert.Equal(new Av1ObuHeader(2, false, 0, 0, 2, 0), Av1ObuHeader.Read([0b0001_0010, 0x00]));

        // The size is a leb128 number: 0x80 0x01 = 128, in its two-byte and in a padded three-byte form
        var payload = new byte[128];
        Assert.Equal(new Av1ObuHeader(6, false, 0, 0, 3, 128), Av1ObuHeader.Read([0b0011_0010, 0x80, 0x01, .. payload]));
        Assert.Equal(new Av1ObuHeader(6, false, 0, 0, 4, 128), Av1ObuHeader.Read([0b0011_0010, 0x80, 0x81, 0x00, .. payload]));
    }

    [Fact]
    public void ExtensionCarriesTheLayerIdentifiers()
    {
        // 0 0110 1 1 0: a frame (6) with an extension and a size field; 101 10 000: temporal_id 5, spatial_id 2
        var header = Av1ObuHeader.Read([0b0011_0110, 0b1011_0000, 0x01, 0x77]);
        Assert.Equal(new Av1ObuHeader(Type: 6, HasExtension: true, TemporalId: 5, SpatialId: 2, HeaderLength: 3, PayloadLength: 1), header);

        // The largest identifiers: 111 11 000
        Assert.Equal(new Av1ObuHeader(3, true, 7, 3, 3, 0), Av1ObuHeader.Read([0b0001_1110, 0b1111_1000, 0x00]));
    }

    [Fact]
    public void UnitWithoutSizeFieldExtendsToTheEndOfTheData()
    {
        // 0 0100 0 0 0: a tile group (4) without extension nor size field
        Assert.Equal(new Av1ObuHeader(4, false, 0, 0, 1, 4), Av1ObuHeader.Read([0b0010_0000, 1, 2, 3, 4]));
        Assert.Equal(new Av1ObuHeader(4, false, 0, 0, 1, 0), Av1ObuHeader.Read([0b0010_0000]));

        // 0 0100 1 0 0 with the extension 001 01 000: the payload starts after the extension byte
        Assert.Equal(new Av1ObuHeader(4, true, 1, 1, 2, 2), Av1ObuHeader.Read([0b0010_0100, 0b0010_1000, 9, 9]));
    }

    [Fact]
    public void ReservedBitsAndReservedTypesAreAccepted()
    {
        // obu_reserved_1bit and extension_header_reserved_3bits are ignored by decoders; reserved types (9 here) are skipped by their size
        Assert.Equal(new Av1ObuHeader(1, false, 0, 0, 2, 0), Av1ObuHeader.Read([0b0000_1011, 0x00]));
        Assert.Equal(new Av1ObuHeader(6, true, 5, 2, 3, 0), Av1ObuHeader.Read([0b0011_0110, 0b1011_0111, 0x00]));
        Assert.Equal(new Av1ObuHeader(9, false, 0, 0, 2, 1), Av1ObuHeader.Read([0b0100_1010, 0x01, 0x00]));
    }

    [Theory]
    [InlineData(new byte[] { 0b1000_1010, 0x00 })] // forbidden bit
    [InlineData(new byte[] { 0b1111_1111, 0x00, 0x00 })]
    [InlineData(new byte[] { 0b0000_1010, 0x04, 1, 2, 3 })] // obu_size 4, 3 bytes of payload
    [InlineData(new byte[] { 0b0000_1010, 0x01 })] // obu_size 1, no payload
    [InlineData(new byte[] { 0b0000_1010, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0x00 })] // obu_size 2^32 - 1
    [InlineData(new byte[] { 0b0000_1010 })] // the size field is missing
    [InlineData(new byte[] { 0b0000_1010, 0x80 })] // the size field is truncated
    [InlineData(new byte[] { 0b0000_1010, 0x80, 0x80, 0x80, 0x80, 0x10, 0x00 })] // the size does not fit in 32 bits
    [InlineData(new byte[] { 0b0011_0110 })] // the extension byte is missing
    [InlineData(new byte[] { 0b0011_0110, 0b1011_0000 })] // the size field is missing after the extension
    [InlineData(new byte[] { })]
    public void MalformedHeadersAreRejected(byte[] data)
    {
        var exception = Assert.Throws<InvalidImageContentException>(() => Av1ObuHeader.Read(data));
        Assert.Equal(ImageFormat.Avif, exception.Format);
    }
}
