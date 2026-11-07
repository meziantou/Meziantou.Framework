namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The patches of a frame (ISO/IEC 18181-1, sections C.4.5 and K.3): rectangles of a reference frame that are blended
/// onto the frame at given positions, before its color transform. An encoder stores what repeats in an image, such as
/// the letters of a text, once in a reference frame.
/// </summary>
/// <remarks>
/// The reference decoder bounds the number of patches, not their size: a few bytes can describe a million patches that
/// each cover the whole frame. The total area of the patches is bounded by <see cref="MaxFrameCoverage"/> times the area of
/// the frame, plus <see cref="AreaAllowance"/> samples, so that blending them costs about as much as decoding the frame.
/// An encoder stores what repeats once, so the patches of an image rarely cover more than the frame.
/// </remarks>
internal sealed class JpegXlPatchDictionary : IDisposable
{
    internal const int MaxFrameCoverage = 16;
    internal const ulong AreaAllowance = 1UL << 20;

    private const int ContextCount = 10;
    private const int ContextReferenceCount = 0;
    private const int ContextReferenceFrame = 1;
    private const int ContextSize = 2;
    private const int ContextReferencePosition = 3;
    private const int ContextPosition = 4;
    private const int ContextBlendMode = 5;
    private const int ContextOffset = 6;
    private const int ContextCount2 = 7;
    private const int ContextAlphaChannel = 8;
    private const int ContextClamp = 9;

    // A source is five values: the reference frame, the position in it and the size. A patch is three values: its source
    // and its position in the frame, followed by one packed blending per channel group
    private const int SourceFieldCount = 5;
    private const int PatchFieldCount = 3;

    private readonly AllocationScope _scope;
    private readonly int _blendingsPerPatch;
    private Av1PooledArray<int>? _sources;
    private Av1PooledArray<int>? _patches;
    private int _sourceCount;
    private int _patchCount;

    private JpegXlPatchDictionary(AllocationScope scope, int extraChannelCount)
    {
        _scope = scope;
        _blendingsPerPatch = extraChannelCount + 1;
    }

    /// <summary>Reads the patches of a frame, at the start of <c>LfGlobal</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="width">The width of the frame, rounded up to a multiple of 8.</param>
    /// <param name="height">The height of the frame, rounded up to a multiple of 8.</param>
    /// <param name="extraChannelCount">The number of extra channels of the image.</param>
    /// <param name="scope">The scope of the patches.</param>
    public static JpegXlPatchDictionary Read(JpegXlBitReader reader, int width, int height, int extraChannelCount, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(scope);
        JpegXlPatchDictionary? owned = null;
        try
        {
            var result = owned = new JpegXlPatchDictionary(scope, extraChannelCount);
            result.ReadCore(reader, width, height, extraChannelCount);
            owned = null;
            return result;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>Blends the patches onto a frame, in the order in which they are stored.</summary>
    /// <param name="image">The header of the image.</param>
    /// <param name="frame">The samples of the frame, before its color transform.</param>
    /// <param name="references">The reference frames.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public void Apply(JpegXlImageHeader image, JpegXlImageBuffer frame, ReadOnlySpan<JpegXlImageBuffer?> references, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(frame);
        if (_patchCount == 0)
            return;

        var sources = _sources!.Span;
        var patches = _patches!.Span;
        var patchLength = PatchFieldCount + _blendingsPerPatch;
        var colorCount = frame.ColorChannelCount;
        for (var i = 0; i < _sourceCount; i++)
        {
            // A patch copies samples that the color transform has not converted, from a frame of the same channels
            var source = sources.Slice(i * SourceFieldCount, SourceFieldCount);
            var reference = references[source[0]];
            if (reference is null || !reference.IsBeforeColorTransform || reference.ColorChannelCount != colorCount)
                throw JpegXlFormat.Invalid("a patch copies from a reference frame that does not exist or that was kept after its color transform.");

            if ((long)source[1] + source[3] > reference.Width || (long)source[2] + source[4] > reference.Height)
                throw JpegXlFormat.Invalid("a patch copies from outside its reference frame.");
        }

        using var scratch = Av1PooledArray<float>.Rent(_scope, (long)frame.ChannelCount * frame.Width, clear: false);
        Span<JpegXlChannelBlending> blendings = stackalloc JpegXlChannelBlending[_blendingsPerPatch];
        var rowCount = 0;
        for (var i = 0; i < _patchCount; i++)
        {
            var patch = patches.Slice(i * patchLength, patchLength);
            var source = sources.Slice(patch[0] * SourceFieldCount, SourceFieldCount);
            var reference = references[source[0]]!;
            for (var j = 0; j < _blendingsPerPatch; j++)
            {
                var packed = patch[PatchFieldCount + j];
                blendings[j] = new JpegXlChannelBlending(packed & 0xFF, (packed >> 8) & 0xFFFF, (packed >> 24) != 0);
            }

            // The part of the patch that is on the frame: a patch may reach the padding of the frame
            var x = patch[1];
            var y = patch[2];
            var width = Math.Min(source[3], frame.Width - x);
            var height = Math.Min(source[4], frame.Height - y);
            if (width <= 0)
                continue;

            for (var row = 0; row < height; row++)
            {
                if ((rowCount++ & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                JpegXlBlending.BlendRow(image, frame, x, y + row, reference, source[1], source[2] + row, width, colorCount, blendings, scratch.Span);
            }
        }
    }

    public void Dispose()
    {
        _sources?.Dispose();
        _sources = null;
        _patches?.Dispose();
        _patches = null;
    }

    private void ReadCore(JpegXlBitReader reader, int width, int height, int extraChannelCount)
    {
        using var code = JpegXlEntropyCode.Read(reader, ContextCount, _scope);
        using var decoder = new JpegXlEntropyDecoder(code, _scope);
        decoder.Begin(reader);

        // A value may take no bit: the counts are bounded by the size of the frame
        var maxSourceCount = 1024 + ((long)width * height / 4);
        var maxPatchCount = maxSourceCount * 4;
        var sourceCount = decoder.Read(reader, ContextReferenceCount);
        if (sourceCount > maxSourceCount)
            throw JpegXlFormat.Invalid("a frame has too many patches.");

        // A patch is inside the frame, so its area is at most that of the frame: the total stays far from overflowing
        var areaLimit = (Math.Min((ulong)width * (ulong)height, 1UL << 40) * MaxFrameCoverage) + AreaAllowance;
        ulong totalArea = 0;

        var patchLength = PatchFieldCount + _blendingsPerPatch;
        Span<int> source = stackalloc int[SourceFieldCount];
        Span<int> patch = stackalloc int[patchLength];
        for (var i = 0; i < sourceCount; i++)
        {
            var reference = decoder.Read(reader, ContextReferenceFrame);
            if (reference >= 4)
                throw JpegXlFormat.Invalid("a patch copies from a reference frame that does not exist.");

            source[0] = (int)reference;
            source[1] = ReadCoordinate(reader, decoder, ContextReferencePosition);
            source[2] = ReadCoordinate(reader, decoder, ContextReferencePosition);
            source[3] = ReadCoordinate(reader, decoder, ContextSize) + 1;
            source[4] = ReadCoordinate(reader, decoder, ContextSize) + 1;
            Append(ref _sources, _sourceCount * SourceFieldCount, source);
            var sourceIndex = _sourceCount++;

            var count = (long)decoder.Read(reader, ContextCount2) + 1;
            if (_patchCount + count > maxPatchCount)
                throw JpegXlFormat.Invalid("a frame has too many patches.");

            long x = 0;
            long y = 0;
            for (var j = 0; j < count; j++)
            {
                if (j == 0)
                {
                    x = decoder.Read(reader, ContextPosition);
                    y = decoder.Read(reader, ContextPosition);
                }
                else
                {
                    // The position of the previous patch of the same source, plus an offset
                    x += JpegXlBitReader.UnpackSigned(decoder.Read(reader, ContextOffset));
                    y += JpegXlBitReader.UnpackSigned(decoder.Read(reader, ContextOffset));
                }

                if (x < 0 || y < 0 || x + source[3] > width || y + source[4] > height)
                    throw JpegXlFormat.Invalid("a patch is outside its frame.");

                totalArea += (ulong)source[3] * (ulong)source[4];
                if (totalArea > areaLimit)
                    throw JpegXlFormat.Invalid("the patches of a frame cover too large an area.");

                patch[0] = sourceIndex;
                patch[1] = (int)x;
                patch[2] = (int)y;
                for (var k = 0; k < _blendingsPerPatch; k++)
                {
                    var mode = decoder.Read(reader, ContextBlendMode);
                    if (mode >= JpegXlChannelBlending.ModeCount)
                        throw JpegXlFormat.Invalid("a patch has a blend mode that does not exist.");

                    var blending = new JpegXlChannelBlending((int)mode, 0, Clamp: false);
                    uint alpha = 0;
                    if (blending.UsesAlpha && extraChannelCount > 1)
                    {
                        alpha = decoder.Read(reader, ContextAlphaChannel);
                        if (alpha >= extraChannelCount)
                            throw JpegXlFormat.Invalid("a patch is blended with an alpha channel that does not exist.");
                    }

                    var clamp = (blending.UsesAlpha || mode == JpegXlChannelBlending.ModeMultiply) && decoder.Read(reader, ContextClamp) != 0;
                    patch[PatchFieldCount + k] = (int)mode | ((int)alpha << 8) | (clamp ? 1 << 24 : 0);
                }

                Append(ref _patches, _patchCount * patchLength, patch);
                _patchCount++;
                if ((_patchCount & 1023) == 0)
                {
                    reader.EnsureNotOverrun();
                }
            }

            reader.EnsureNotOverrun();
        }

        decoder.EnsureFinalState();
    }

    private static int ReadCoordinate(JpegXlBitReader reader, JpegXlEntropyDecoder decoder, int context)
    {
        var value = decoder.Read(reader, context);
        if (value >= 1u << 30)
            throw JpegXlFormat.Invalid("a patch has a position or a size that is too large.");

        return (int)value;
    }

    /// <summary>Appends values to a list that grows by doubling, in memory charged to the scope.</summary>
    private void Append(ref Av1PooledArray<int>? list, int count, ReadOnlySpan<int> values)
    {
        if (list is null || count + values.Length > list.Length)
        {
            var larger = Av1PooledArray<int>.Rent(_scope, Math.Max(256, (long)(count + values.Length) * 2), clear: false);
            list?.Span[..count].CopyTo(larger.Span);
            list?.Dispose();
            list = larger;
        }

        values.CopyTo(list.Span[count..]);
    }
}
