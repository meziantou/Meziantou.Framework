using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The endpoint and selector indices of the ETC1S blocks of one BasisLZ image (Appendix D.5.3): its RGB slice and, when
/// the texture has one, its second slice. They are kept for the next P-frame of an animation, which refers to them.
/// </summary>
internal sealed class Etc1SIndices : IDisposable
{
    // Appendix D.5.3: the constants of the block decoding
    private const int EndpointPredRepeatLastSymbol = (4 * 4 * 4 * 4) + 1 - 1;
    private const int EndpointPredMinRepeatCount = 3;
    private const int SelectorHistoryRleCountThreshold = 3;
    private const int SelectorHistoryRleCountTotal = 1 << 6;

    private readonly Etc1SCodebooks _codebooks;
    private readonly int _columns;
    private PooledBuffer? _color;
    private PooledBuffer? _alpha;

    private Etc1SIndices(Etc1SCodebooks codebooks, int columns, PooledBuffer color, PooledBuffer? alpha)
    {
        _codebooks = codebooks;
        _columns = columns;
        _color = color;
        _alpha = alpha;
    }

    public static Etc1SIndices Decode(RandomAccessSource source, Ktx2Texture texture, Etc1SCodebooks codebooks, Ktx2Level level, Ktx2BasisImage image, int columns, int rows, Etc1SIndices? previous, ImageCodecContext context)
    {
        if (image.IsPFrame && previous is null)
            throw Ktx2Format.Invalid("a BasisLZ P-frame has no previous frame.");

        var blockCount = Ktx2Format.Multiply(columns, rows);
        var length = Ktx2Format.ToBufferLength(Ktx2Format.Multiply(blockCount, 4), context.Limits);
        PooledBuffer? color = null;
        PooledBuffer? alpha = null;
        try
        {
            color = context.Scope.Rent(length, AllocationKind.DecoderState, clear: false);
            using (var slice = source.ReadToBuffer(level.Offset + image.RgbOffset, image.RgbLength))
            {
                DecodeSlice(slice.Span, codebooks, columns, rows, image.IsPFrame, previous?._color, color.Span, context);
            }

            if (texture.BasisSecondChannelId >= 0)
            {
                alpha = context.Scope.Rent(length, AllocationKind.DecoderState, clear: false);
                using var slice = source.ReadToBuffer(level.Offset + image.AlphaOffset, image.AlphaLength);
                DecodeSlice(slice.Span, codebooks, columns, rows, image.IsPFrame, previous?._alpha, alpha.Span, context);
            }

            var indices = new Etc1SIndices(codebooks, columns, color, alpha);
            color = null;
            alpha = null;
            return indices;
        }
        finally
        {
            color?.Dispose();
            alpha?.Dispose();
        }
    }

    /// <summary>Writes the 8-bit RGBA texels of a row of blocks: the colors of the first slice, and the red of the second slice as alpha.</summary>
    public void DecodeRow(int row, Span<byte> strip, int stride)
    {
        var color = unsafe(MemoryMarshal.Cast<byte, ushort>(_color!.Span));
        var alpha = _alpha is null ? default : unsafe(MemoryMarshal.Cast<byte, ushort>(_alpha.Span));
        for (var column = 0; column < _columns; column++)
        {
            var block = (row * _columns) + column;
            var destination = strip[(column * 16)..];
            WriteBlock(color[block * 2], color[(block * 2) + 1], destination, stride, alphaOnly: false);
            if (_alpha is not null)
            {
                WriteBlock(alpha[block * 2], alpha[(block * 2) + 1], destination, stride, alphaOnly: true);
            }
        }
    }

    public void Dispose()
    {
        _color?.Dispose();
        _color = null;
        _alpha?.Dispose();
        _alpha = null;
    }

    private void WriteBlock(int endpointIndex, int selectorIndex, Span<byte> destination, int stride, bool alphaOnly)
    {
        // An ETC1S block is an ETC1 block in differential mode with no delta, one intensity table and no flip (section 21.1);
        // a selector value 0 to 3 is the modifier -b, -a, +a, +b
        var endpoint = _codebooks.Endpoints.Slice(endpointIndex * 4, 4);
        var selector = _codebooks.Selectors.Slice(selectorIndex * 4, 4);
        var table = Etc1SModifiers.Slice(endpoint[3] * 4, 4);
        for (var y = 0; y < 4; y++)
        {
            var bits = selector[y];
            for (var x = 0; x < 4; x++)
            {
                var modifier = table[(bits >> (2 * x)) & 3];
                var texel = destination.Slice((y * stride) + (x * 4), 4);
                if (alphaOnly)
                {
                    texel[3] = (byte)Math.Clamp(endpoint[0] + modifier, 0, 255);
                    continue;
                }

                texel[0] = (byte)Math.Clamp(endpoint[0] + modifier, 0, 255);
                texel[1] = (byte)Math.Clamp(endpoint[1] + modifier, 0, 255);
                texel[2] = (byte)Math.Clamp(endpoint[2] + modifier, 0, 255);
                texel[3] = 255;
            }
        }
    }

    // The intensity modifiers of ETC1 (Table 142), each row in the order -b, -a, +a, +b
    private static ReadOnlySpan<short> Etc1SModifiers =>
    [
        -8, -2, 2, 8,
        -17, -5, 5, 17,
        -29, -9, 9, 29,
        -42, -13, 13, 42,
        -60, -18, 18, 60,
        -80, -24, 24, 80,
        -106, -33, 33, 106,
        -183, -47, 47, 183,
    ];

    /// <summary>Decodes the endpoint and selector indices of the blocks of a slice (Appendix D.5.3).</summary>
    private static void DecodeSlice(ReadOnlySpan<byte> data, Etc1SCodebooks codebooks, int columns, int rows, bool isPFrame, PooledBuffer? previous, Span<byte> output, ImageCodecContext context)
    {
        var indices = unsafe(MemoryMarshal.Cast<byte, ushort>(output));
        var previousIndices = previous is null ? default : unsafe(MemoryMarshal.Cast<byte, ushort>(previous.Span));
        var reader = new Etc1SBitReader(data);
        var endpointCount = codebooks.EndpointCount;
        var selectorCount = codebooks.SelectorCount;
        var historySize = codebooks.SelectorHistorySize;
        var historyRleSymbol = selectorCount + historySize;
        // The state of a wide slice is rented from the scope, charged like the other buffers of the decoder
        using var historyBuffer = historySize <= 1024 ? null : context.Scope.Rent(historySize * sizeof(int), AllocationKind.DecoderState, clear: true);
        using var predictionsBuffer = columns <= 1024 ? null : context.Scope.Rent(2 * columns * sizeof(ushort), AllocationKind.DecoderState, clear: true);
        using var predictionBitsBuffer = columns <= 2048 ? null : context.Scope.Rent((columns + 1) >> 1, AllocationKind.DecoderState, clear: true);
        Span<int> history = historyBuffer is null ? stackalloc int[historySize] : unsafe(MemoryMarshal.Cast<byte, int>(historyBuffer.Span));
        history.Clear();
        var rover = historySize / 2;
        Span<ushort> predictions = predictionsBuffer is null ? stackalloc ushort[2 * columns] : unsafe(MemoryMarshal.Cast<byte, ushort>(predictionsBuffer.Span));
        Span<byte> predictionBits = predictionBitsBuffer is null ? stackalloc byte[(columns + 1) >> 1] : predictionBitsBuffer.Span;
        var selectorRunCount = 0;
        var currentPredBits = 0;
        var previousPredSymbol = 0;
        var predRepeatCount = 0;
        var previousEndpoint = 0;
        for (var y = 0; y < rows; y++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var currentRow = y & 1;
            for (var x = 0; x < columns; x++)
            {
                if ((x & 1) == 0)
                {
                    if ((y & 1) == 0)
                    {
                        if (predRepeatCount > 0)
                        {
                            predRepeatCount--;
                            currentPredBits = previousPredSymbol;
                        }
                        else
                        {
                            currentPredBits = codebooks.EndpointPredModel.Decode(ref reader);
                            if (currentPredBits == EndpointPredRepeatLastSymbol)
                            {
                                predRepeatCount = (int)Math.Min(reader.ReadVariableLength(4) + EndpointPredMinRepeatCount - 1, int.MaxValue);
                                currentPredBits = previousPredSymbol;
                            }
                            else if (currentPredBits > EndpointPredRepeatLastSymbol)
                            {
                                throw Ktx2Format.Invalid("an endpoint prediction symbol of the BasisLZ data is out of range.");
                            }
                            else
                            {
                                previousPredSymbol = currentPredBits;
                            }
                        }

                        predictionBits[x >> 1] = (byte)(currentPredBits >> 4);
                    }
                    else
                    {
                        currentPredBits = predictionBits[x >> 1];
                    }
                }

                var pred = currentPredBits & 3;
                currentPredBits >>= 2;
                int endpointIndex;
                var selectorIndex = 0;
                switch (pred)
                {
                    case 0:
                        if (x == 0)
                            throw Ktx2Format.Invalid("a BasisLZ block of the first column reuses the endpoint of the block on its left.");

                        endpointIndex = previousEndpoint;
                        break;
                    case 1:
                        if (y == 0)
                            throw Ktx2Format.Invalid("a BasisLZ block of the first row reuses the endpoint of the block above it.");

                        endpointIndex = predictions[((currentRow ^ 1) * columns) + x];
                        break;
                    case 2:
                        if (isPFrame)
                        {
                            endpointIndex = previousIndices[((y * columns) + x) * 2];
                            selectorIndex = previousIndices[(((y * columns) + x) * 2) + 1];
                        }
                        else
                        {
                            if (x == 0 || y == 0)
                                throw Ktx2Format.Invalid("a BasisLZ block of the first row or column reuses the endpoint of the block above and on its left.");

                            endpointIndex = predictions[((currentRow ^ 1) * columns) + x - 1];
                        }

                        break;
                    default:
                        endpointIndex = codebooks.EndpointDeltaModel.Decode(ref reader) + previousEndpoint;
                        if (endpointIndex >= endpointCount)
                        {
                            endpointIndex -= endpointCount;
                        }

                        break;
                }

                if (endpointIndex >= endpointCount)
                    throw Ktx2Format.Invalid("a BasisLZ block refers to an endpoint that is not in the codebook.");

                predictions[(currentRow * columns) + x] = (ushort)endpointIndex;
                previousEndpoint = endpointIndex;
                if (!isPFrame || pred != 2)
                {
                    int selectorSymbol;
                    if (selectorRunCount > 0)
                    {
                        selectorRunCount--;
                        selectorSymbol = selectorCount;
                    }
                    else
                    {
                        selectorSymbol = codebooks.SelectorModel.Decode(ref reader);
                        if (selectorSymbol == historyRleSymbol)
                        {
                            var runSymbol = codebooks.SelectorHistoryRleModel.Decode(ref reader);
                            selectorRunCount = runSymbol == SelectorHistoryRleCountTotal - 1
                                ? (int)Math.Min(reader.ReadVariableLength(7) + SelectorHistoryRleCountThreshold, int.MaxValue)
                                : runSymbol + SelectorHistoryRleCountThreshold;
                            selectorSymbol = selectorCount;
                            selectorRunCount--;
                        }
                        else if (selectorSymbol > historyRleSymbol)
                        {
                            throw Ktx2Format.Invalid("a selector symbol of the BasisLZ data is out of range.");
                        }
                    }

                    if (selectorSymbol >= selectorCount)
                    {
                        // A reference into the selector history (an approximate move-to-front list, Appendix D.5.1)
                        var historyIndex = selectorSymbol - selectorCount;
                        if (historyIndex >= historySize)
                            throw Ktx2Format.Invalid("a BasisLZ block refers to the selector history, which is empty or shorter.");

                        selectorIndex = history[historyIndex];
                        if (historyIndex != 0)
                        {
                            (history[historyIndex / 2], history[historyIndex]) = (history[historyIndex], history[historyIndex / 2]);
                        }
                    }
                    else
                    {
                        selectorIndex = selectorSymbol;
                        if (historySize > 0)
                        {
                            history[rover++] = selectorIndex;
                            if (rover == historySize)
                            {
                                rover = historySize / 2;
                            }
                        }
                    }
                }

                if (selectorIndex >= selectorCount)
                    throw Ktx2Format.Invalid("a BasisLZ block refers to a selector that is not in the codebook.");

                indices[((y * columns) + x) * 2] = (ushort)endpointIndex;
                indices[(((y * columns) + x) * 2) + 1] = (ushort)selectorIndex;
            }
        }
    }
}
