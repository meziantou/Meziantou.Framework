namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The global codebooks of BasisLZ (KTX Appendix C and D.2 to D.4): the ETC1S endpoints (a 5:5:5 color and an intensity
/// table), the selectors (16 values of 2 bits), the four Huffman tables of the slices and the size of the selector history.
/// </summary>
internal sealed class Etc1SCodebooks : IDisposable
{
    private PooledBuffer? _endpoints;
    private PooledBuffer? _selectors;

    private Etc1SCodebooks()
    {
    }

    public int EndpointCount { get; private init; }

    public int SelectorCount { get; private init; }

    public required Etc1SHuffmanTable EndpointPredModel { get; init; }

    public required Etc1SHuffmanTable EndpointDeltaModel { get; init; }

    public required Etc1SHuffmanTable SelectorModel { get; init; }

    public required Etc1SHuffmanTable SelectorHistoryRleModel { get; init; }

    public int SelectorHistorySize { get; private init; }

    /// <summary>Gets the endpoints: red, green, blue (expanded to 8 bits) and the intensity table, 4 bytes each.</summary>
    public ReadOnlySpan<byte> Endpoints => _endpoints!.Span[..(EndpointCount * 4)];

    /// <summary>Gets the selectors: 4 bytes each, a row of 4 values of 2 bits per byte, the left one in the low bits.</summary>
    public ReadOnlySpan<byte> Selectors => _selectors!.Span[..(SelectorCount * 4)];

    public static Etc1SCodebooks Read(RandomAccessSource source, Ktx2Texture texture, ImageCodecContext context)
    {
        var (endpointCount, selectorCount, endpointsLength, selectorsLength, tablesLength) = texture.BasisCodebooks;
        var dataOffset = texture.GlobalData.Offset + Ktx2Format.BasisLzGlobalHeaderLength + ((long)texture.BasisImages.Length * Ktx2Format.BasisLzImageDescLength);
        PooledBuffer? endpoints = null;
        PooledBuffer? selectors = null;
        try
        {
            endpoints = context.Scope.Rent(endpointCount * 4, AllocationKind.DecoderState, clear: false);
            selectors = context.Scope.Rent(selectorCount * 4, AllocationKind.DecoderState, clear: false);
            using (var data = source.ReadToBuffer(dataOffset, endpointsLength))
            {
                ReadEndpoints(data.Span, endpointCount, endpoints.Span);
            }

            using (var data = source.ReadToBuffer(dataOffset + endpointsLength, selectorsLength))
            {
                ReadSelectors(data.Span, selectorCount, selectors.Span);
            }

            using var tables = source.ReadToBuffer(dataOffset + endpointsLength + selectorsLength, tablesLength);
            var reader = new Etc1SBitReader(tables.Span);
            var codebooks = new Etc1SCodebooks
            {
                EndpointCount = endpointCount,
                SelectorCount = selectorCount,
                EndpointPredModel = Etc1SHuffmanTable.Read(ref reader),
                EndpointDeltaModel = Etc1SHuffmanTable.Read(ref reader),
                SelectorModel = Etc1SHuffmanTable.Read(ref reader),
                SelectorHistoryRleModel = Etc1SHuffmanTable.Read(ref reader),
                SelectorHistorySize = (int)reader.ReadBits(13),
            };
            codebooks._endpoints = endpoints;
            codebooks._selectors = selectors;
            endpoints = null;
            selectors = null;
            return codebooks;
        }
        finally
        {
            endpoints?.Dispose();
            selectors?.Dispose();
        }
    }

    public void Dispose()
    {
        _endpoints?.Dispose();
        _endpoints = null;
        _selectors?.Dispose();
        _selectors = null;
    }

    private static void ReadEndpoints(ReadOnlySpan<byte> data, int count, Span<byte> endpoints)
    {
        // Appendix D.2: four Huffman tables and the grayscale flag, then the DPCM of the intensities and the colors
        var reader = new Etc1SBitReader(data);
        var colorModel0 = Etc1SHuffmanTable.Read(ref reader);
        var colorModel1 = Etc1SHuffmanTable.Read(ref reader);
        var colorModel2 = Etc1SHuffmanTable.Read(ref reader);
        var intensityModel = Etc1SHuffmanTable.Read(ref reader);
        var grayscale = reader.ReadBit() != 0;
        var intensity = 0;
        Span<int> previous = [16, 16, 16];
        for (var i = 0; i < count; i++)
        {
            intensity = (intensity + intensityModel.Decode(ref reader)) & 7;
            var channels = grayscale ? 1 : 3;
            for (var c = 0; c < channels; c++)
            {
                var model = previous[c] switch
                {
                    <= 9 => colorModel0,
                    <= 21 => colorModel1,
                    _ => colorModel2,
                };
                previous[c] = (previous[c] + model.Decode(ref reader)) & 31;
            }

            if (grayscale)
            {
                previous[1] = previous[0];
                previous[2] = previous[0];
            }

            var endpoint = endpoints.Slice(i * 4, 4);
            endpoint[0] = Extend5(previous[0]);
            endpoint[1] = Extend5(previous[1]);
            endpoint[2] = Extend5(previous[2]);
            endpoint[3] = (byte)intensity;
        }
    }

    private static void ReadSelectors(ReadOnlySpan<byte> data, int count, Span<byte> selectors)
    {
        // Appendix D.3: two zero bits, the raw flag, then raw selectors or XOR deltas of the previous one
        var reader = new Etc1SBitReader(data);
        if (reader.ReadBits(2) != 0)
            throw Ktx2Format.Invalid("the reserved bits of the BasisLZ selector codebook are not 0.");

        if (reader.ReadBit() != 0)
        {
            for (var i = 0; i < count * 4; i++)
            {
                selectors[i] = (byte)reader.ReadBits(8);
            }

            return;
        }

        var deltaModel = Etc1SHuffmanTable.Read(ref reader);
        for (var j = 0; j < 4; j++)
        {
            selectors[j] = (byte)reader.ReadBits(8);
        }

        for (var i = 1; i < count; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                var delta = deltaModel.Decode(ref reader);
                if (delta > 255)
                    throw Ktx2Format.Invalid("a selector delta of the BasisLZ data is larger than a byte.");

                selectors[(i * 4) + j] = (byte)(delta ^ selectors[((i - 1) * 4) + j]);
            }
        }
    }

    private static byte Extend5(int value) => (byte)((value << 3) | (value >> 2));
}
