using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The block decoders on blocks spelled out bit by bit, whose texels are computed by hand from the Khronos Data Format
/// Specification 1.3.1 (sections 18 to 20). The corpus compares whole surfaces with DirectXTex and OpenImageIO.
/// </summary>
public sealed class BlockDecoderTests
{
    [Fact]
    public void Bc1FourColorModeInterpolatesThirds()
    {
        // color0 = pure red (0xF800) > color1 = pure blue (0x001F); codes 0, 1, 2, 3 on the first row
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0b11_10_01_00, 0, 0, 0];
        var texels = new byte[64];
        BlockDecoder.DecodeBc1(block, texels);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, texels[0..4]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, texels[4..8]);

        // (2 × 31/31 + 0) / 3 = 2/3 → 170; (0 + 2 × 31/31) / 3 → 170 of blue
        Assert.Equal(new byte[] { 170, 0, 85, 255 }, texels[8..12]);
        Assert.Equal(new byte[] { 85, 0, 170, 255 }, texels[12..16]);

        // The other rows are code 0
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, texels[16..20]);
    }

    [Fact]
    public void Bc1ThreeColorModeHasTransparentBlack()
    {
        // color0 = 0x001F (blue) <= color1 = 0xF800 (red): code 2 is the half, code 3 transparent black
        byte[] block = [0x1F, 0x00, 0x00, 0xF8, 0b11_10_01_00, 0, 0, 0];
        var texels = new byte[64];
        BlockDecoder.DecodeBc1(block, texels);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, texels[0..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, texels[4..8]);

        // (31/31 + 0) / 2 × 255 = 127.5: the exact half rounds up
        Assert.Equal(new byte[] { 128, 0, 128, 255 }, texels[8..12]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, texels[12..16]);
    }

    [Fact]
    public void Bc1ExpandsFiveAndSixBitEndpointsToTheNearestByte()
    {
        // 28/31 × 255 = 230.3 (bit replication would give 231); 21/63 × 255 = 85
        var color = (ushort)((28 << 11) | (21 << 5) | 1);
        byte[] block = [(byte)color, (byte)(color >> 8), 0, 0, 0, 0, 0, 0];
        var texels = new byte[64];
        BlockDecoder.DecodeBc1(block, texels);
        Assert.Equal(new byte[] { 230, 85, 8, 255 }, texels[0..4]);
    }

    [Fact]
    public void Bc2AlphaIsFourBitsTimes17AndColorsUseFourColors()
    {
        // Alpha nibbles 0, 1, 15 for the first three texels; the colors of BC1 with color0 <= color1 still use four colors
        byte[] block = [0x10, 0x0F, 0, 0, 0, 0, 0, 0, 0x1F, 0x00, 0x00, 0xF8, 0b00_00_11_10, 0, 0, 0];
        var texels = new byte[64];
        BlockDecoder.DecodeBc2(block, texels);
        Assert.Equal(0, texels[3]);
        Assert.Equal(17, texels[7]);
        Assert.Equal(255, texels[11]);

        // Code 2 is (2 × blue + red) / 3, code 3 is (blue + 2 × red) / 3: never transparent
        Assert.Equal(new byte[] { 85, 0, 170 }, texels[0..3]);
        Assert.Equal(new byte[] { 170, 0, 85 }, texels[4..7]);
    }

    [Fact]
    public void RgtcChannelHasEightAndSixValueModes()
    {
        // red0 = 200 > red1 = 10: (6 × 200 + 10) / 7 = 172.86 → 173, ..., (200 + 6 × 10) / 7 = 37.14 → 37
        var samples = new byte[16];
        BlockDecoder.DecodeUnsignedChannel(Channel(200, 10, [0, 1, 2, 3, 4, 5, 6, 7]), samples, stride: 1);
        Assert.Equal(new byte[] { 200, 10, 173, 146, 119, 91, 64, 37 }, samples[..8]);

        // red0 = 10 <= red1 = 200: fifths, then 0 and 255
        BlockDecoder.DecodeUnsignedChannel(Channel(10, 200, [0, 1, 2, 3, 4, 5, 6, 7]), samples, stride: 1);
        Assert.Equal(new byte[] { 10, 200, 48, 86, 124, 162, 0, 255 }, samples[..8]);
    }

    [Fact]
    public void SignedRgtcChannelReadsMinus128AsMinus127AndRoundsToMultiplesOf1Over127()
    {
        var samples = new float[16];

        // red0 = -9 > red1 = -57: (2 × -9 + 5 × -57) / 7 = -43.29 → -43 (code 6)
        BlockDecoder.DecodeSignedChannel(Channel(unchecked((byte)-9), unchecked((byte)-57), [0, 1, 6]), samples, stride: 1);
        Assert.Equal((float)(-9 / 127.0), samples[0]);
        Assert.Equal((float)(-57 / 127.0), samples[1]);
        Assert.Equal((float)(-43 / 127.0), samples[2]);

        // red0 = -128 is compared as -128 (so it is <= red1 = -127, the six-value mode) and read as -1
        BlockDecoder.DecodeSignedChannel(Channel(0x80, 0x81, [0, 1, 6, 7]), samples, stride: 1);
        Assert.Equal(-1f, samples[0]);
        Assert.Equal(-1f, samples[1]);
        Assert.Equal(-1f, samples[2]);
        Assert.Equal(1f, samples[3]);
    }

    [Fact]
    public void Bc7ReservedLowByteDecodesToZero()
    {
        var block = new byte[16];
        block[1] = 0xFF;
        var texels = Enumerable.Repeat((byte)0xAA, 64).ToArray();
        BlockDecoder.DecodeBc7(block, texels);
        Assert.All(texels, value => value == 0);
    }

    [Fact]
    public void Bc7Mode6InterpolatesSevenBitEndpointsWithPBits()
    {
        // Mode 6 (bit 6 set): R0 = 0, R1 = 127, G and B and A as R; P-bits 0 and 1; every 4-bit index 15 (the anchor has 3 bits, 7)
        var bits = new System.Collections.BitArray(128);
        bits[6] = true;
        var position = 7;
        foreach (var value in new[] { 0, 127, 0, 127, 0, 127, 0, 127 })
        {
            for (var i = 0; i < 7; i++)
                bits[position++] = ((value >> i) & 1) != 0;
        }

        bits[position++] = false;
        bits[position++] = true;
        Assert.Equal(65, position);
        while (position < 128)
            bits[position++] = true;

        var block = new byte[16];
        bits.CopyTo(block, 0);
        var texels = new byte[64];
        BlockDecoder.DecodeBc7(block, texels);

        // E0 = (0 << 1 | 0) → 0; E1 = (127 << 1 | 1) = 255. Texel 0 has index 7 (weight 30): (34 × 0 + 30 × 255 + 32) >> 6 = 120
        Assert.Equal(new byte[] { 120, 120, 120, 120 }, texels[0..4]);

        // The other texels have index 15 (weight 64): 255
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, texels[4..8]);
    }

    [Fact]
    public void Bc6HReservedModeDecodesToOpaqueBlack()
    {
        var block = new byte[16];
        block[0] = 0x13;
        var texels = new ushort[64];
        BlockDecoder.DecodeBc6H(block, texels, isSigned: false);
        Assert.Equal(new ushort[] { 0, 0, 0, 0x3C00 }, texels[0..4]);
    }

    [Fact]
    public void Bc6HMode3UnquantizesTenBitEndpoints()
    {
        // Mode 0x03: endpoints of 10 bits, not transformed; E0 = 0, E1 = 1023; every index bit set
        var bits = new System.Collections.BitArray(128);
        bits[0] = true;
        bits[1] = true;
        for (var i = 35; i < 65; i++)
            bits[i] = true;

        for (var i = 65; i < 128; i++)
            bits[i] = true;

        var block = new byte[16];
        bits.CopyTo(block, 0);
        var texels = new ushort[64];
        BlockDecoder.DecodeBc6H(block, texels, isSigned: false);

        // 1023 is the maximum: 0xFFFF. Texel 0 (3-bit index 7, weight 30): (30 × 65535 + 32) >> 6 = 30720, × 31 >> 6 = 14880
        Assert.Equal(new ushort[] { 14880, 14880, 14880, 0x3C00 }, texels[0..4]);

        // Index 15 (weight 64): 65535 × 31 >> 6 = 31743, the half 65504
        Assert.Equal(new ushort[] { 31743, 31743, 31743, 0x3C00 }, texels[4..8]);
        Assert.Equal(65504f, (float)BitConverter.UInt16BitsToHalf(texels[4]));

        // The signed format: 1023 is the 10-bit value -1, whose magnitude 1 unquantizes to (1 << 15 + 0x4000) >> 9 = 96
        BlockDecoder.DecodeBc6H(block, texels, isSigned: true);
        Assert.Equal((ushort)(((96 * 31) >> 5) | 0x8000), texels[4]);
    }

    /// <summary>An RGTC channel: two endpoints and 3-bit codes for the first texels.</summary>
    private static byte[] Channel(byte red0, byte red1, int[] codes)
    {
        ulong bits = 0;
        for (var i = 0; i < codes.Length; i++)
            bits |= (ulong)codes[i] << (3 * i);

        var block = new byte[8];
        block[0] = red0;
        block[1] = red1;
        for (var i = 0; i < 6; i++)
            block[2 + i] = (byte)(bits >> (8 * i));

        return block;
    }
}
