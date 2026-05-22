using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.SnapshotTesting;

internal static class PngImageLoader
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly int[] Adam7XStarts = [0, 4, 0, 2, 0, 1, 0];
    private static readonly int[] Adam7YStarts = [0, 0, 4, 0, 2, 0, 1];
    private static readonly int[] Adam7XSteps = [8, 8, 4, 4, 2, 2, 1];
    private static readonly int[] Adam7YSteps = [8, 8, 8, 4, 4, 2, 2];
    private static readonly uint[] PngCrc32Table = InitializePngCrc32Table();

    internal static bool IsPng(ReadOnlySpan<byte> data)
    {
        return data.StartsWith(PngSignature);
    }

    internal static Image Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < PngSignature.Length)
            throw new InvalidDataException("The PNG data is too small.");

        if (!data.StartsWith(PngSignature))
            throw new InvalidDataException("Invalid PNG signature.");

        var offset = PngSignature.Length;
        var seenIhdr = false;
        var seenPlte = false;
        var seenIdat = false;
        var seenIend = false;
        var seenTrns = false;
        var idatEnded = false;

        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlaceMethod = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        ushort transparentGray = 0;
        var hasTransparentGray = false;
        ushort transparentRed = 0;
        ushort transparentGreen = 0;
        ushort transparentBlue = 0;
        var hasTransparentRgb = false;

        using var idatData = new MemoryStream();
        while (offset < data.Length)
        {
            var chunkLengthValue = ReadUInt32BigEndian(data, offset);
            if (chunkLengthValue > int.MaxValue)
                throw new InvalidDataException("The PNG chunk length is too large.");

            var chunkLength = (int)chunkLengthValue;
            var chunkTypeOffset = offset + 4;
            var chunkDataOffset = offset + 8;
            var chunkCrcOffset = checked(chunkDataOffset + chunkLength);
            if (chunkCrcOffset > data.Length - 4)
                throw new InvalidDataException("The PNG chunk is truncated.");

            var chunkType = data[chunkTypeOffset..(chunkTypeOffset + 4)];
            var chunkData = data[chunkDataOffset..chunkCrcOffset];
            var expectedChunkCrc = ReadUInt32BigEndian(data, chunkCrcOffset);
            var actualChunkCrc = ComputePngCrc32(chunkType, chunkData);
            if (expectedChunkCrc != actualChunkCrc)
                throw new InvalidDataException("The PNG chunk CRC is invalid.");

            offset = chunkCrcOffset + 4;

            if (!seenIhdr && !chunkType.SequenceEqual("IHDR"u8))
                throw new InvalidDataException("The PNG IHDR chunk must be the first chunk.");

            if (chunkType.SequenceEqual("IHDR"u8))
            {
                if (seenIhdr)
                    throw new InvalidDataException("The PNG IHDR chunk is duplicated.");

                if (chunkData.Length != 13)
                    throw new InvalidDataException("The PNG IHDR chunk length is invalid.");

                var widthValue = ReadUInt32BigEndian(chunkData, 0);
                var heightValue = ReadUInt32BigEndian(chunkData, 4);
                if (widthValue > int.MaxValue || heightValue > int.MaxValue)
                    throw new NotSupportedException("Unsupported PNG dimensions.");

                width = (int)widthValue;
                height = (int)heightValue;
                bitDepth = chunkData[8];
                colorType = chunkData[9];
                var compressionMethod = chunkData[10];
                var filterMethod = chunkData[11];
                interlaceMethod = chunkData[12];

                ValidatePngHeader(width, height, bitDepth, colorType, compressionMethod, filterMethod, interlaceMethod);
                seenIhdr = true;
                continue;
            }

            if (chunkType.SequenceEqual("PLTE"u8))
            {
                if (!seenIhdr || seenPlte || seenIdat)
                    throw new InvalidDataException("The PNG PLTE chunk is invalid.");

                if (colorType is 0 or 4)
                    throw new NotSupportedException("PLTE is not allowed for grayscale PNG images.");

                if (chunkData.Length == 0 || chunkData.Length % 3 != 0)
                    throw new InvalidDataException("The PNG PLTE chunk length is invalid.");

                var paletteEntryCount = chunkData.Length / 3;
                if (paletteEntryCount > 256)
                    throw new InvalidDataException("The PNG PLTE chunk contains too many entries.");

                if (colorType == 3 && paletteEntryCount > (1 << bitDepth))
                    throw new InvalidDataException("The PNG PLTE chunk contains too many entries for the bit depth.");

                palette = chunkData.ToArray();
                seenPlte = true;
                continue;
            }

            if (chunkType.SequenceEqual("tRNS"u8))
            {
                if (!seenIhdr || seenIdat || seenTrns)
                    throw new InvalidDataException("The PNG tRNS chunk is invalid.");

                seenTrns = true;
                switch (colorType)
                {
                    case 0:
                        if (chunkData.Length != 2)
                            throw new InvalidDataException("The PNG tRNS chunk length is invalid for grayscale PNG images.");

                        transparentGray = ReadUInt16BigEndian(chunkData, 0);
                        hasTransparentGray = true;
                        break;
                    case 2:
                        if (chunkData.Length != 6)
                            throw new InvalidDataException("The PNG tRNS chunk length is invalid for truecolor PNG images.");

                        transparentRed = ReadUInt16BigEndian(chunkData, 0);
                        transparentGreen = ReadUInt16BigEndian(chunkData, 2);
                        transparentBlue = ReadUInt16BigEndian(chunkData, 4);
                        hasTransparentRgb = true;
                        break;
                    case 3:
                    {
                        if (!seenPlte || palette is null)
                            throw new InvalidDataException("The PNG tRNS chunk requires a preceding PLTE chunk.");

                        var paletteEntryCount = palette.Length / 3;
                        if (chunkData.Length > paletteEntryCount)
                            throw new InvalidDataException("The PNG tRNS chunk length is invalid for indexed PNG images.");

                        paletteAlpha = chunkData.ToArray();
                        break;
                    }
                    default:
                        throw new NotSupportedException("The PNG tRNS chunk is not allowed for this color type.");
                }

                continue;
            }

            if (chunkType.SequenceEqual("IDAT"u8))
            {
                if (!seenIhdr)
                    throw new InvalidDataException("The PNG IDAT chunk appears before IHDR.");

                if (idatEnded)
                    throw new InvalidDataException("The PNG IDAT chunks must be consecutive.");

                if (colorType == 3 && palette is null)
                    throw new InvalidDataException("Indexed PNG images require a PLTE chunk before IDAT.");

                idatData.Write(chunkData);
                seenIdat = true;
                continue;
            }

            if (chunkType.SequenceEqual("IEND"u8))
            {
                if (chunkData.Length != 0)
                    throw new InvalidDataException("The PNG IEND chunk length is invalid.");

                seenIend = true;
                break;
            }

            if (chunkType.SequenceEqual("acTL"u8) || chunkType.SequenceEqual("fcTL"u8) || chunkType.SequenceEqual("fdAT"u8))
                throw new NotSupportedException("Animated PNG (APNG) is not supported.");

            if (seenIdat)
                idatEnded = true;

            if (IsPngCriticalChunk(chunkType))
                throw new NotSupportedException($"Unsupported critical PNG chunk: {GetChunkTypeName(chunkType)}");
        }

        if (!seenIhdr)
            throw new InvalidDataException("The PNG IHDR chunk is missing.");

        if (!seenIdat)
            throw new InvalidDataException("The PNG IDAT chunk is missing.");

        if (!seenIend)
            throw new InvalidDataException("The PNG IEND chunk is missing.");

        if (offset != data.Length)
            throw new InvalidDataException("The PNG data has trailing bytes.");

        var bitsPerPixel = checked(GetPngChannelCount(colorType) * bitDepth);
        var decompressedData = DecompressPngIdatData(idatData.GetBuffer().AsSpan(0, checked((int)idatData.Length)));
        var expectedImageDataLength = GetExpectedPngImageDataLength(width, height, bitsPerPixel, interlaceMethod);
        if (decompressedData.Length != expectedImageDataLength)
            throw new InvalidDataException("The PNG decompressed image data size is invalid.");

        var pixels = new Argb[checked(width * height)];
        if (interlaceMethod == 0)
        {
            DecodePngRows(
                decompressedData,
                width,
                height,
                bitsPerPixel,
                colorType,
                bitDepth,
                palette,
                paletteAlpha,
                transparentGray,
                hasTransparentGray,
                transparentRed,
                transparentGreen,
                transparentBlue,
                hasTransparentRgb,
                pixels);
        }
        else
        {
            DecodePngInterlacedRows(
                decompressedData,
                width,
                height,
                bitsPerPixel,
                colorType,
                bitDepth,
                palette,
                paletteAlpha,
                transparentGray,
                hasTransparentGray,
                transparentRed,
                transparentGreen,
                transparentBlue,
                hasTransparentRgb,
                pixels);
        }

        return Image.Create(width, height, pixels);
    }

    private static byte[] DecompressPngIdatData(ReadOnlySpan<byte> compressedData)
    {
        using var compressedStream = new MemoryStream(compressedData.ToArray());
        using var zlibStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlibStream.CopyTo(output);

        return output.ToArray();
    }

    private static void DecodePngRows(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        int bitsPerPixel,
        byte colorType,
        byte bitDepth,
        byte[]? palette,
        byte[]? paletteAlpha,
        ushort transparentGray,
        bool hasTransparentGray,
        ushort transparentRed,
        ushort transparentGreen,
        ushort transparentBlue,
        bool hasTransparentRgb,
        Argb[] pixels)
    {
        var bytesPerPixel = GetPngFilterBytesPerPixel(colorType, bitDepth);
        var rowLength = GetPngScanlineLength(width, bitsPerPixel);
        var currentDataOffset = 0;
        var previousRow = new byte[rowLength];
        var currentRow = new byte[rowLength];

        for (var y = 0; y < height; y++)
        {
            var filterType = data[currentDataOffset];
            currentDataOffset++;
            var rowData = data[currentDataOffset..(currentDataOffset + rowLength)];
            currentDataOffset += rowLength;

            UnfilterPngRow(filterType, rowData, previousRow, bytesPerPixel, currentRow);
            DecodePngScanline(
                currentRow,
                width,
                colorType,
                bitDepth,
                palette,
                paletteAlpha,
                transparentGray,
                hasTransparentGray,
                transparentRed,
                transparentGreen,
                transparentBlue,
                hasTransparentRgb,
                pixels.AsSpan(y * width, width));

            (previousRow, currentRow) = (currentRow, previousRow);
        }
    }

    private static void DecodePngInterlacedRows(
        ReadOnlySpan<byte> data,
        int width,
        int height,
        int bitsPerPixel,
        byte colorType,
        byte bitDepth,
        byte[]? palette,
        byte[]? paletteAlpha,
        ushort transparentGray,
        bool hasTransparentGray,
        ushort transparentRed,
        ushort transparentGreen,
        ushort transparentBlue,
        bool hasTransparentRgb,
        Argb[] pixels)
    {
        var bytesPerPixel = GetPngFilterBytesPerPixel(colorType, bitDepth);
        var currentDataOffset = 0;

        for (var pass = 0; pass < Adam7XStarts.Length; pass++)
        {
            var passWidth = GetAdam7PassDimension(width, Adam7XStarts[pass], Adam7XSteps[pass]);
            var passHeight = GetAdam7PassDimension(height, Adam7YStarts[pass], Adam7YSteps[pass]);
            if (passWidth == 0 || passHeight == 0)
                continue;

            var rowLength = GetPngScanlineLength(passWidth, bitsPerPixel);
            var previousRow = new byte[rowLength];
            var currentRow = new byte[rowLength];
            var decodedPassRow = new Argb[passWidth];
            for (var passY = 0; passY < passHeight; passY++)
            {
                var filterType = data[currentDataOffset];
                currentDataOffset++;
                var rowData = data[currentDataOffset..(currentDataOffset + rowLength)];
                currentDataOffset += rowLength;

                UnfilterPngRow(filterType, rowData, previousRow, bytesPerPixel, currentRow);
                DecodePngScanline(
                    currentRow,
                    passWidth,
                    colorType,
                    bitDepth,
                    palette,
                    paletteAlpha,
                    transparentGray,
                    hasTransparentGray,
                    transparentRed,
                    transparentGreen,
                    transparentBlue,
                    hasTransparentRgb,
                    decodedPassRow);

                var destinationY = Adam7YStarts[pass] + (passY * Adam7YSteps[pass]);
                for (var passX = 0; passX < passWidth; passX++)
                {
                    var destinationX = Adam7XStarts[pass] + (passX * Adam7XSteps[pass]);
                    pixels[(destinationY * width) + destinationX] = decodedPassRow[passX];
                }

                (previousRow, currentRow) = (currentRow, previousRow);
            }
        }
    }

    private static void DecodePngScanline(
        ReadOnlySpan<byte> rowData,
        int pixelCount,
        byte colorType,
        byte bitDepth,
        byte[]? palette,
        byte[]? paletteAlpha,
        ushort transparentGray,
        bool hasTransparentGray,
        ushort transparentRed,
        ushort transparentGreen,
        ushort transparentBlue,
        bool hasTransparentRgb,
        Span<Argb> destinationPixels)
    {
        for (var x = 0; x < pixelCount; x++)
        {
            destinationPixels[x] = DecodePngPixel(
                rowData,
                x,
                colorType,
                bitDepth,
                palette,
                paletteAlpha,
                transparentGray,
                hasTransparentGray,
                transparentRed,
                transparentGreen,
                transparentBlue,
                hasTransparentRgb);
        }
    }

    private static Argb DecodePngPixel(
        ReadOnlySpan<byte> rowData,
        int pixelIndex,
        byte colorType,
        byte bitDepth,
        byte[]? palette,
        byte[]? paletteAlpha,
        ushort transparentGray,
        bool hasTransparentGray,
        ushort transparentRed,
        ushort transparentGreen,
        ushort transparentBlue,
        bool hasTransparentRgb)
    {
        switch (colorType)
        {
            case 0:
            {
                var graySample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 1, sampleIndexInPixel: 0);
                var gray = NormalizePngSampleToByte(graySample, bitDepth);
                var alpha = hasTransparentGray && graySample == transparentGray ? (byte)0 : (byte)255;
                return new Argb(alpha, gray, gray, gray);
            }
            case 2:
            {
                var rSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 3, sampleIndexInPixel: 0);
                var gSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 3, sampleIndexInPixel: 1);
                var bSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 3, sampleIndexInPixel: 2);
                var r = NormalizePngSampleToByte(rSample, bitDepth);
                var g = NormalizePngSampleToByte(gSample, bitDepth);
                var b = NormalizePngSampleToByte(bSample, bitDepth);
                var alpha = hasTransparentRgb && rSample == transparentRed && gSample == transparentGreen && bSample == transparentBlue ? (byte)0 : (byte)255;
                return new Argb(alpha, r, g, b);
            }
            case 3:
            {
                if (palette is null)
                    throw new InvalidDataException("Indexed PNG images require a palette.");

                var paletteIndex = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 1, sampleIndexInPixel: 0);
                if (paletteIndex >= palette.Length / 3)
                    throw new InvalidDataException("The PNG palette index is out of range.");

                var paletteOffset = checked((int)paletteIndex * 3);
                var r = palette[paletteOffset];
                var g = palette[paletteOffset + 1];
                var b = palette[paletteOffset + 2];
                var alpha = paletteAlpha is not null && paletteIndex < paletteAlpha.Length ? paletteAlpha[paletteIndex] : (byte)255;
                return new Argb(alpha, r, g, b);
            }
            case 4:
            {
                var graySample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 2, sampleIndexInPixel: 0);
                var alphaSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 2, sampleIndexInPixel: 1);
                var gray = NormalizePngSampleToByte(graySample, bitDepth);
                var alpha = NormalizePngSampleToByte(alphaSample, bitDepth);
                return new Argb(alpha, gray, gray, gray);
            }
            case 6:
            {
                var rSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 4, sampleIndexInPixel: 0);
                var gSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 4, sampleIndexInPixel: 1);
                var bSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 4, sampleIndexInPixel: 2);
                var aSample = ReadPngSample(rowData, pixelIndex, bitDepth, samplesPerPixel: 4, sampleIndexInPixel: 3);
                var r = NormalizePngSampleToByte(rSample, bitDepth);
                var g = NormalizePngSampleToByte(gSample, bitDepth);
                var b = NormalizePngSampleToByte(bSample, bitDepth);
                var a = NormalizePngSampleToByte(aSample, bitDepth);
                return new Argb(a, r, g, b);
            }
            default:
                throw new NotSupportedException("Unsupported PNG color type.");
        }
    }

    private static ushort ReadPngSample(ReadOnlySpan<byte> rowData, int pixelIndex, byte bitDepth, int samplesPerPixel, int sampleIndexInPixel)
    {
        if (bitDepth < 8)
        {
            var sampleIndex = checked((pixelIndex * samplesPerPixel) + sampleIndexInPixel);
            return ReadPngPackedSample(rowData, sampleIndex, bitDepth);
        }

        if (bitDepth == 8)
        {
            var sampleOffset = checked((pixelIndex * samplesPerPixel) + sampleIndexInPixel);
            return rowData[sampleOffset];
        }

        var sampleByteOffset = checked(((pixelIndex * samplesPerPixel) + sampleIndexInPixel) * 2);
        return BinaryPrimitives.ReadUInt16BigEndian(rowData[sampleByteOffset..(sampleByteOffset + 2)]);
    }

    private static ushort ReadPngPackedSample(ReadOnlySpan<byte> rowData, int sampleIndex, byte bitDepth)
    {
        var samplesPerByte = 8 / bitDepth;
        var byteIndex = sampleIndex / samplesPerByte;
        var sampleOffsetInByte = sampleIndex % samplesPerByte;
        var shift = 8 - ((sampleOffsetInByte + 1) * bitDepth);
        var mask = (1 << bitDepth) - 1;
        return (ushort)((rowData[byteIndex] >> shift) & mask);
    }

    private static byte NormalizePngSampleToByte(ushort sample, byte bitDepth)
    {
        if (bitDepth == 16)
            return (byte)(sample >> 8);

        if (bitDepth == 8)
            return (byte)sample;

        var maxValue = (1 << bitDepth) - 1;
        return (byte)((sample * 255 + (maxValue / 2)) / maxValue);
    }

    private static void ValidatePngHeader(int width, int height, byte bitDepth, byte colorType, byte compressionMethod, byte filterMethod, byte interlaceMethod)
    {
        if (width <= 0 || height <= 0)
            throw new NotSupportedException("Unsupported PNG dimensions.");

        if (compressionMethod != 0)
            throw new NotSupportedException("Unsupported PNG compression method.");

        if (filterMethod != 0)
            throw new NotSupportedException("Unsupported PNG filter method.");

        if (interlaceMethod is not 0 and not 1)
            throw new NotSupportedException("Unsupported PNG interlace method.");

        var bitDepthValid = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false,
        };

        if (!bitDepthValid)
            throw new NotSupportedException("Unsupported PNG bit depth for the color type.");
    }

    private static int GetPngChannelCount(byte colorType)
    {
        return colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException("Unsupported PNG color type."),
        };
    }

    private static int GetPngFilterBytesPerPixel(byte colorType, byte bitDepth)
    {
        var channelCount = GetPngChannelCount(colorType);
        var bitsPerPixel = checked(channelCount * bitDepth);
        return Math.Max(1, (bitsPerPixel + 7) / 8);
    }

    private static int GetPngScanlineLength(int width, int bitsPerPixel)
    {
        return checked((width * bitsPerPixel + 7) / 8);
    }

    private static int GetExpectedPngImageDataLength(int width, int height, int bitsPerPixel, byte interlaceMethod)
    {
        if (interlaceMethod == 0)
            return checked(height * (1 + GetPngScanlineLength(width, bitsPerPixel)));

        var totalLength = 0;
        for (var pass = 0; pass < Adam7XStarts.Length; pass++)
        {
            var passWidth = GetAdam7PassDimension(width, Adam7XStarts[pass], Adam7XSteps[pass]);
            var passHeight = GetAdam7PassDimension(height, Adam7YStarts[pass], Adam7YSteps[pass]);
            if (passWidth == 0 || passHeight == 0)
                continue;

            var rowLength = GetPngScanlineLength(passWidth, bitsPerPixel);
            totalLength = checked(totalLength + passHeight * (1 + rowLength));
        }

        return totalLength;
    }

    private static int GetAdam7PassDimension(int fullLength, int start, int step)
    {
        if (fullLength <= start)
            return 0;

        return ((fullLength - start) + step - 1) / step;
    }

    private static void UnfilterPngRow(byte filterType, ReadOnlySpan<byte> rowData, ReadOnlySpan<byte> previousRow, int bytesPerPixel, Span<byte> destination)
    {
        switch (filterType)
        {
            case 0:
                rowData.CopyTo(destination);
                break;
            case 1:
                for (var i = 0; i < rowData.Length; i++)
                {
                    var left = i >= bytesPerPixel ? destination[i - bytesPerPixel] : (byte)0;
                    destination[i] = (byte)(rowData[i] + left);
                }

                break;
            case 2:
                for (var i = 0; i < rowData.Length; i++)
                {
                    var up = previousRow.Length > 0 ? previousRow[i] : (byte)0;
                    destination[i] = (byte)(rowData[i] + up);
                }

                break;
            case 3:
                for (var i = 0; i < rowData.Length; i++)
                {
                    var left = i >= bytesPerPixel ? destination[i - bytesPerPixel] : (byte)0;
                    var up = previousRow.Length > 0 ? previousRow[i] : (byte)0;
                    destination[i] = (byte)(rowData[i] + ((left + up) >> 1));
                }

                break;
            case 4:
                for (var i = 0; i < rowData.Length; i++)
                {
                    var left = i >= bytesPerPixel ? destination[i - bytesPerPixel] : (byte)0;
                    var up = previousRow.Length > 0 ? previousRow[i] : (byte)0;
                    var upperLeft = i >= bytesPerPixel && previousRow.Length > 0 ? previousRow[i - bytesPerPixel] : (byte)0;
                    destination[i] = (byte)(rowData[i] + PaethPredictor(left, up, upperLeft));
                }

                break;
            default:
                throw new NotSupportedException("Unsupported PNG filter type.");
        }
    }

    private static byte PaethPredictor(byte left, byte up, byte upperLeft)
    {
        var p = left + up - upperLeft;
        var pa = Math.Abs(p - left);
        var pb = Math.Abs(p - up);
        var pc = Math.Abs(p - upperLeft);

        if (pa <= pb && pa <= pc)
            return left;

        if (pb <= pc)
            return up;

        return upperLeft;
    }

    private static bool IsPngCriticalChunk(ReadOnlySpan<byte> chunkType)
    {
        return (chunkType[0] & 0x20) == 0;
    }

    private static string GetChunkTypeName(ReadOnlySpan<byte> chunkType)
    {
        return new string([(char)chunkType[0], (char)chunkType[1], (char)chunkType[2], (char)chunkType[3]]);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> chunkData)
    {
        var crc = uint.MaxValue;
        crc = UpdatePngCrc32(crc, chunkType);
        crc = UpdatePngCrc32(crc, chunkData);
        return ~crc;
    }

    private static uint UpdatePngCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = PngCrc32Table[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] InitializePngCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
            }

            table[index] = crc;
        }

        return table;
    }

    private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 4 > data.Length)
            throw new InvalidDataException("The image data is truncated.");

        return BinaryPrimitives.ReadUInt32BigEndian(data[offset..(offset + 4)]);
    }

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 2 > data.Length)
            throw new InvalidDataException("The image data is truncated.");

        return BinaryPrimitives.ReadUInt16BigEndian(data[offset..(offset + 2)]);
    }
}
