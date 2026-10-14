using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Parses and validates the marker segments of a JPEG 2000 codestream (T.800, Annex A). Every method takes the content of
/// a marker segment: the bytes that follow its length field.
/// </summary>
internal static class Jpeg2000MarkerReader
{
    private const int SizFixedLength = 36;

    /// <summary>Parses a SIZ marker segment (A.5.1).</summary>
    public static Jpeg2000Siz ReadSiz(ReadOnlySpan<byte> content)
    {
        if (content.Length < SizFixedLength + 3)
            throw Invalid("the SIZ marker segment is too short.");

        var capabilities = BinaryPrimitives.ReadUInt16BigEndian(content);
        long xSize = BinaryPrimitives.ReadUInt32BigEndian(content[2..]);
        long ySize = BinaryPrimitives.ReadUInt32BigEndian(content[6..]);
        long xOffset = BinaryPrimitives.ReadUInt32BigEndian(content[10..]);
        long yOffset = BinaryPrimitives.ReadUInt32BigEndian(content[14..]);
        long tileWidth = BinaryPrimitives.ReadUInt32BigEndian(content[18..]);
        long tileHeight = BinaryPrimitives.ReadUInt32BigEndian(content[22..]);
        long tileXOffset = BinaryPrimitives.ReadUInt32BigEndian(content[26..]);
        long tileYOffset = BinaryPrimitives.ReadUInt32BigEndian(content[30..]);
        int componentCount = BinaryPrimitives.ReadUInt16BigEndian(content[34..]);
        if (componentCount is < 1 or > 16384)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the image declares {componentCount} components; 1 to 16384 are allowed."));

        if (content.Length != SizFixedLength + (3 * componentCount))
            throw Invalid("the length of the SIZ marker segment does not match its number of components.");

        if (xSize == 0 || ySize == 0)
            throw Invalid("the reference grid is empty.");

        if (tileWidth == 0 || tileHeight == 0)
            throw Invalid("the tiles are empty.");

        if (xOffset >= xSize || yOffset >= ySize)
            throw Invalid("the image area is empty: its offset is outside the reference grid.");

        // The first tile holds the first sample of the image area
        if (tileXOffset > xOffset || tileYOffset > yOffset || tileXOffset + tileWidth <= xOffset || tileYOffset + tileHeight <= yOffset)
            throw Invalid("the first tile does not hold the origin of the image area.");

        var components = new Jpeg2000ComponentSize[componentCount];
        for (var i = 0; i < componentCount; i++)
        {
            var entry = content.Slice(SizFixedLength + (3 * i), 3);
            var precision = (entry[0] & 0x7F) + 1;
            if (precision > 38)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the component {i} declares samples of {precision} bits; at most 38 are allowed."));

            if (entry[1] == 0 || entry[2] == 0)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the component {i} has a sub-sampling factor of 0."));

            components[i] = new Jpeg2000ComponentSize(precision, (entry[0] & 0x80) != 0, entry[1], entry[2]);
        }

        var siz = new Jpeg2000Siz(capabilities, xSize, ySize, xOffset, yOffset, tileWidth, tileHeight, tileXOffset, tileYOffset, components);
        if (siz.TilesAcross * siz.TilesDown > Jpeg2000Format.MaxTileCount)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the image has {siz.TilesAcross * siz.TilesDown} tiles; at most {Jpeg2000Format.MaxTileCount} are allowed."));

        return siz;
    }

    /// <summary>Parses a COD marker segment (A.6.1).</summary>
    public static Jpeg2000CodingDefaults ReadCod(ReadOnlySpan<byte> content)
    {
        if (content.Length < 10)
            throw Invalid("the COD marker segment is too short.");

        var style = content[0];
        if ((style & 0xF8) != 0)
            throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream uses a coding style that Rec. ITU-T T.800 does not define.", "coding style");

        var progression = content[1];
        if (progression > Jpeg2000CodingDefaults.ProgressionCprl)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the progression order {progression} is not defined."));

        var layers = BinaryPrimitives.ReadUInt16BigEndian(content[2..]);
        if (layers == 0)
            throw Invalid("the codestream has no layer.");

        var transform = content[4];
        if (transform > 1)
            throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream uses a multiple component transformation of Rec. ITU-T T.801.", "multiple component transformation");

        var componentStyle = ReadComponentStyle(content[5..], (style & 0x01) != 0, "COD");
        return new Jpeg2000CodingDefaults(mayUseSop: (style & 0x02) != 0, usesEph: (style & 0x04) != 0, progression, layers, transform == 1, componentStyle);
    }

    /// <summary>Parses a COC marker segment (A.6.2).</summary>
    public static Jpeg2000CodingStyle ReadCoc(ReadOnlySpan<byte> content, int componentCount, out int component)
    {
        var offset = ReadComponentIndex(content, componentCount, "COC", out component);
        if (content.Length < offset + 6)
            throw Invalid("the COC marker segment is too short.");

        var style = content[offset];
        if ((style & 0xFE) != 0)
            throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream uses a coding style that Rec. ITU-T T.800 does not define.", "coding style");

        return ReadComponentStyle(content[(offset + 1)..], (style & 0x01) != 0, "COC");
    }

    /// <summary>Parses a QCD marker segment (A.6.4).</summary>
    public static Jpeg2000Quantization ReadQcd(ReadOnlySpan<byte> content) => ReadQuantization(content, "QCD");

    /// <summary>Parses a QCC marker segment (A.6.5).</summary>
    public static Jpeg2000Quantization ReadQcc(ReadOnlySpan<byte> content, int componentCount, out int component)
    {
        var offset = ReadComponentIndex(content, componentCount, "QCC", out component);
        return ReadQuantization(content[offset..], "QCC");
    }

    /// <summary>Parses a RGN marker segment (A.6.3) and returns the shift of the region of interest.</summary>
    public static int ReadRgn(ReadOnlySpan<byte> content, int componentCount, out int component)
    {
        var offset = ReadComponentIndex(content, componentCount, "RGN", out component);
        if (content.Length != offset + 2)
            throw Invalid("the length of the RGN marker segment is not the one of its fields.");

        if (content[offset] != 0)
            throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream has a region of interest that is not coded with the Maxshift method.", "region of interest style");

        return content[offset + 1];
    }

    /// <summary>Parses a POC marker segment (A.6.6) and appends its progressions.</summary>
    public static void ReadPoc(ReadOnlySpan<byte> content, int componentCount, List<Jpeg2000ProgressionChange> progressions)
    {
        var wide = componentCount >= 257;
        var entryLength = wide ? 9 : 7;
        if (content.IsEmpty || content.Length % entryLength != 0)
            throw Invalid("the length of the POC marker segment is not a number of progressions.");

        for (var offset = 0; offset < content.Length; offset += entryLength)
        {
            var entry = content.Slice(offset, entryLength);
            int resolutionStart = entry[0];
            int componentStart;
            int layerEnd;
            int resolutionEnd;
            int componentEnd;
            int progression;
            if (wide)
            {
                componentStart = BinaryPrimitives.ReadUInt16BigEndian(entry[1..]);
                layerEnd = BinaryPrimitives.ReadUInt16BigEndian(entry[3..]);
                resolutionEnd = entry[5];
                componentEnd = BinaryPrimitives.ReadUInt16BigEndian(entry[6..]);
                progression = entry[8];
            }
            else
            {
                componentStart = entry[1];
                layerEnd = BinaryPrimitives.ReadUInt16BigEndian(entry[2..]);
                resolutionEnd = entry[4];
                componentEnd = entry[5];
                progression = entry[6];

                // The 8-bit field cannot hold 256: 0 stands for it
                if (componentEnd == 0)
                {
                    componentEnd = 256;
                }
            }

            if (progression > Jpeg2000CodingDefaults.ProgressionCprl)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the progression order {progression} is not defined."));

            if (resolutionEnd > Jpeg2000Format.MaxDecompositionLevels + 1 || resolutionStart >= resolutionEnd || componentStart >= componentEnd || layerEnd == 0)
                throw Invalid("a progression of the POC marker segment has an empty range.");

            progressions.Add(new Jpeg2000ProgressionChange(resolutionStart, componentStart, layerEnd, resolutionEnd, Math.Min(componentEnd, componentCount), progression));
        }
    }

    private static int ReadComponentIndex(ReadOnlySpan<byte> content, int componentCount, string marker, out int component)
    {
        var length = componentCount < 257 ? 1 : 2;
        if (content.Length < length)
            throw Invalid($"the {marker} marker segment is too short.");

        component = length == 1 ? content[0] : BinaryPrimitives.ReadUInt16BigEndian(content);
        if (component >= componentCount)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a {marker} marker segment names the component {component}; the image has {componentCount}."));

        return length;
    }

    private static Jpeg2000CodingStyle ReadComponentStyle(ReadOnlySpan<byte> content, bool hasPrecincts, string marker)
    {
        var levels = content[0];
        if (levels > Jpeg2000Format.MaxDecompositionLevels)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the {marker} marker segment declares {levels} decomposition levels; at most {Jpeg2000Format.MaxDecompositionLevels} are allowed."));

        var widthExponent = content[1] + 2;
        var heightExponent = content[2] + 2;
        if (widthExponent > 10 || heightExponent > 10 || widthExponent + heightExponent > 12)
            throw Invalid($"the {marker} marker segment declares code-blocks larger than 4096 samples or than 1024 samples in one dimension.");

        var blockStyle = content[3];
        var transformation = content[4];
        if (transformation > 1)
            throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream uses a wavelet transformation of Rec. ITU-T T.801.", "wavelet transformation");

        var expected = 5 + (hasPrecincts ? levels + 1 : 0);
        if (content.Length != expected)
            throw Invalid($"the length of the {marker} marker segment is not the one of its fields.");

        byte[]? precincts = null;
        if (hasPrecincts)
        {
            precincts = content[5..].ToArray();
            for (var resolution = 1; resolution < precincts.Length; resolution++)
            {
                // Only the precincts of the lowest resolution level may be one sample wide or high (B.6)
                if ((precincts[resolution] & 0x0F) == 0 || (precincts[resolution] >> 4) == 0)
                    throw Invalid($"the {marker} marker segment declares precincts of one sample above the lowest resolution level.");
            }
        }

        return new Jpeg2000CodingStyle(levels, widthExponent, heightExponent, blockStyle, transformation == 1, precincts);
    }

    private static Jpeg2000Quantization ReadQuantization(ReadOnlySpan<byte> content, string marker)
    {
        if (content.Length < 2)
            throw Invalid($"the {marker} marker segment is too short.");

        var style = content[0] & 0x1F;
        var guardBits = content[0] >> 5;
        var values = content[1..];
        byte[] exponents;
        ushort[] mantissas;
        switch (style)
        {
            case Jpeg2000Quantization.StyleNone:
                exponents = new byte[values.Length];
                mantissas = new ushort[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    exponents[i] = (byte)(values[i] >> 3);
                }

                break;

            case Jpeg2000Quantization.StyleDerived or Jpeg2000Quantization.StyleExpounded:
            {
                if ((values.Length & 1) != 0 || (style == Jpeg2000Quantization.StyleDerived && values.Length != 2))
                    throw Invalid($"the length of the {marker} marker segment is not the one of its fields.");

                var count = values.Length / 2;
                exponents = new byte[count];
                mantissas = new ushort[count];
                for (var i = 0; i < count; i++)
                {
                    var value = BinaryPrimitives.ReadUInt16BigEndian(values[(2 * i)..]);
                    exponents[i] = (byte)(value >> 11);
                    mantissas[i] = (ushort)(value & 0x7FF);
                }

                break;
            }

            default:
                throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream uses a quantization of Rec. ITU-T T.801.", "quantization style");
        }

        if (exponents.Length > (3 * Jpeg2000Format.MaxDecompositionLevels) + 1)
            throw Invalid($"the {marker} marker segment describes more sub-bands than {Jpeg2000Format.MaxDecompositionLevels} decomposition levels have.");

        return new Jpeg2000Quantization(style, guardBits, exponents, mantissas);
    }

    private static InvalidImageContentException Invalid(string message) => Jpeg2000Format.Invalid(message);
}
