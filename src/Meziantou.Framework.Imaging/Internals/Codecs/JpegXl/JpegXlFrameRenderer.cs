using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the decoded frames of an image into the images that are shown (ISO/IEC 18181-1, annexes F and K): the color
/// transform of each frame, its blending with one of the four reference frames, at its position on the image, and the
/// reference frames that the following frames use.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Samples are floating-point values where 1 is the largest value of an integer sample; blending happens in the color
/// encoding of the image, on encoded samples.
/// </description></item>
/// <item><description>
/// A frame that is not the last one and that has no duration, or that names a slot, is kept as a reference: before its
/// color transform when it says so (the patches of later frames copy from it), otherwise once it is blended, at the size
/// of the image. Outside its rectangle, a blended frame shows the reference it is blended with, or zeros.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlFrameRenderer : IDisposable
{
    private const int ReferenceCount = 4;

    private readonly JpegXlImageHeader _image;
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly JpegXlImageBuffer?[] _references = new JpegXlImageBuffer?[ReferenceCount];
    private readonly JpegXlImageBuffer?[] _lfFrames = new JpegXlImageBuffer?[ReferenceCount];
    private readonly JpegXlOutputColor? _outputColor;
    private readonly int _extraChannelCount;
    private readonly float[]?[] _upsamplingKernels = new float[]?[3];

    public JpegXlFrameRenderer(JpegXlImageHeader image, AllocationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(scope);
        _image = image;
        _scope = scope;
        _cancellationToken = cancellationToken;
        _extraChannelCount = image.ExtraChannels.Length;
        _outputColor = image.IsXybEncoded ? JpegXlOutputColor.Create(image) : null;
    }

    /// <summary>Gets the number of color channels of the images that are shown: one for a grayscale image.</summary>
    public int OutputColorChannelCount => _image.ColorEncoding.IsGray ? 1 : 3;

    /// <summary>Gets the LF frame that holds the LF image of the frames of a level: the one of the level above.</summary>
    /// <param name="level">The level of the frame that uses it: 0 for a frame that is not an LF frame.</param>
    public JpegXlImageBuffer? GetLfFrame(int level) => (uint)level < ReferenceCount ? _lfFrames[level] : null;

    /// <summary>Determines whether a frame is combined with a reference frame, or does not cover the image.</summary>
    public static bool NeedsBlending(JpegXlFrameHeader frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.IsRegular)
            return false;

        if (!frame.IsFullFrame)
            return true;

        if (frame.Blending.Mode != JpegXlBlendingInfo.ModeReplace)
            return true;

        foreach (var blending in frame.ExtraChannelBlending)
        {
            if (blending.Mode != JpegXlBlendingInfo.ModeReplace)
                return true;
        }

        return false;
    }

    /// <summary>Determines whether a later frame can refer to a frame.</summary>
    public static bool CanBeReferenced(JpegXlFrameHeader frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return !frame.IsLast && frame.Type != JpegXlFrameHeader.TypeLf && (frame.Duration == 0 || frame.SaveAsReference != 0);
    }

    /// <summary>Renders a decoded frame.</summary>
    /// <param name="decoded">The samples of the frame; the color channels of a VarDCT frame are taken from it.</param>
    /// <param name="frame">The header of the frame.</param>
    /// <param name="isDisplayed">Whether the frame is shown.</param>
    /// <param name="visibleFrameIndex">The number of frames that are shown up to this one, this one included: a seed of the noise.</param>
    /// <param name="invisibleFrameIndex">The number of frames that are not shown since the last frame that is shown, this one included.</param>
    /// <returns>The image to show, of the size of the image, which the caller disposes; <see langword="null"/> when the frame is not shown.</returns>
    public JpegXlImageBuffer? Render(JpegXlDecodedFrame decoded, JpegXlFrameHeader frame, bool isDisplayed, uint visibleFrameIndex, uint invisibleFrameIndex)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(frame);
        var canBeReferenced = CanBeReferenced(frame);
        JpegXlImageBuffer? owned = null;
        try
        {
            // Patches and splines belong to the samples the frame stores: they are drawn before the frame is upsampled
            var deferUpsampling = frame.Upsampling != 1 && (decoded.Patches is not null || decoded.Splines is not null);
            var buffer = owned = CreateFrameBuffer(decoded, frame, deferUpsampling);
            decoded.Patches?.Apply(_image, buffer, _references, _cancellationToken);
            decoded.Splines?.Draw(buffer, decoded.BaseCorrelationX, decoded.BaseCorrelationB, _cancellationToken);
            if (deferUpsampling)
            {
                var upsampled = Upsample(buffer, frame);
                owned = upsampled;
                buffer.Dispose();
                buffer = upsampled;
            }

            if (decoded.NoiseStrengths is { } noiseStrengths)
            {
                if (buffer.ColorChannelCount != 3)
                    throw JpegXlFormat.Invalid("a frame without three color channels has noise.");

                JpegXlNoise.Apply(buffer, noiseStrengths, (int)frame.GroupDimension, visibleFrameIndex, invisibleFrameIndex, decoded.BaseCorrelationX, decoded.BaseCorrelationB, _scope, _cancellationToken);
            }

            if (frame.Type == JpegXlFrameHeader.TypeLf)
            {
                // An LF frame is the LF image of the frames of the level below: its samples before the color transform
                if (buffer.ColorChannelCount != 3)
                    throw JpegXlFormat.Invalid("an LF frame does not have three color channels.");

                owned = null;
                _lfFrames[frame.LfLevel - 1]?.Dispose();
                _lfFrames[frame.LfLevel - 1] = buffer;
                return null;
            }

            if (canBeReferenced && frame.SaveBeforeColorTransform)
            {
                SetReference(frame.SaveAsReference, buffer.Clone(_scope));
            }

            ApplyColorTransform(buffer, frame);
            if (NeedsBlending(frame))
            {
                var canvas = Blend(buffer, frame);
                owned = canvas;
                buffer.Dispose();
                buffer = canvas;
            }

            if (canBeReferenced && !frame.SaveBeforeColorTransform)
            {
                if (isDisplayed)
                {
                    SetReference(frame.SaveAsReference, buffer.Clone(_scope));
                }
                else
                {
                    owned = null;
                    SetReference(frame.SaveAsReference, buffer);
                }
            }

            if (!isDisplayed)
                return null;

            if (buffer.Width != (int)_image.Width || buffer.Height != (int)_image.Height)
                throw JpegXlFormat.Invalid("a frame that is shown does not have the size of the image.");

            owned = null;
            return buffer;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < _references.Length; i++)
        {
            _references[i]?.Dispose();
            _references[i] = null;
            _lfFrames[i]?.Dispose();
            _lfFrames[i] = null;
        }
    }

    private void SetReference(int slot, JpegXlImageBuffer buffer)
    {
        _references[slot]?.Dispose();
        _references[slot] = buffer;
    }

    /// <summary>
    /// Gathers the samples of a frame, at the size of the frame: the color channels of a VarDCT frame as they are, the
    /// Modular channels scaled by their depth; a channel that the frame stores at a lower resolution is upsampled.
    /// </summary>
    /// <param name="decoded">The decoded frame.</param>
    /// <param name="frame">The frame header.</param>
    /// <param name="deferUpsampling">Whether the samples are left at the size the frame stores: every channel then has that size.</param>
    private JpegXlImageBuffer CreateFrameBuffer(JpegXlDecodedFrame decoded, JpegXlFrameHeader frame, bool deferUpsampling)
    {
        var codedWidth = (int)frame.CodedWidth;
        var codedHeight = (int)frame.CodedHeight;
        var width = deferUpsampling ? codedWidth : (int)frame.Width;
        var height = deferUpsampling ? codedHeight : (int)frame.Height;
        var upsampling = deferUpsampling ? 1 : frame.Upsampling;
        var colorChannelCount = decoded.HasPlanes ? 3 : decoded.ModularColorChannelCount;
        JpegXlImageBuffer? owned = null;
        try
        {
            var buffer = owned = new JpegXlImageBuffer(width, height, colorChannelCount, _extraChannelCount) { IsBeforeColorTransform = true };
            if (decoded.HasPlanes)
            {
                for (var c = 0; c < 3; c++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    AddColorPlane(buffer, c, decoded.DetachPlane(c), decoded.GetPlaneWidth(c), codedWidth, codedHeight, upsampling);
                }
            }
            else if (colorChannelCount != 0)
            {
                AddModularColorChannels(decoded, frame, buffer, codedWidth, codedHeight, upsampling);
            }

            for (var i = 0; i < _extraChannelCount; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var channel = colorChannelCount + i;
                buffer.Allocate(channel, _scope);
                var scale = 1f / ((1 << _image.ExtraChannels[i].BitDepth.BitsPerSample) - 1);
                AddModularChannel(decoded.Modular.Channels[decoded.ModularColorChannelCount + i], buffer, channel, scale, deferUpsampling ? 1 : frame.ExtraChannelUpsampling[i]);
            }

            owned = null;
            return buffer;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>Upsamples every channel of a buffer that has the size the frame stores to the size of the frame.</summary>
    private JpegXlImageBuffer Upsample(JpegXlImageBuffer source, JpegXlFrameHeader frame)
    {
        JpegXlImageBuffer? owned = null;
        try
        {
            var buffer = owned = new JpegXlImageBuffer((int)frame.Width, (int)frame.Height, source.ColorChannelCount, source.ChannelCount - source.ColorChannelCount) { IsBeforeColorTransform = source.IsBeforeColorTransform };
            var kernels = GetKernels(frame.Upsampling);
            for (var c = 0; c < source.ChannelCount; c++)
            {
                buffer.Allocate(c, _scope);
                JpegXlUpsampler.Upsample(source.GetPlane(c), source.GetStride(c), source.Width, source.Height, frame.Upsampling, kernels, buffer.GetRow(c, 0, buffer.Height), buffer.Width, buffer.Height, _cancellationToken);
                source.Release(c);
            }

            owned = null;
            return buffer;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>Gives a color channel, at the resolution the frame stores, to the buffer: as it is, or upsampled.</summary>
    private void AddColorPlane(JpegXlImageBuffer buffer, int channel, Av1PooledArray<float> samples, int stride, int codedWidth, int codedHeight, int upsampling)
    {
        if (upsampling == 1)
        {
            buffer.Adopt(channel, samples, stride);
            return;
        }

        using (samples)
        {
            buffer.Allocate(channel, _scope);
            JpegXlUpsampler.Upsample(samples.Span, stride, codedWidth, codedHeight, upsampling, GetKernels(upsampling), buffer.GetRow(channel, 0, buffer.Height), buffer.Width, buffer.Height, _cancellationToken);
        }
    }

    /// <summary>
    /// Converts the color channels of a Modular frame to floating-point samples: integers scaled by their depth, or, for an
    /// image stored in XYB, the channels Y, X and B minus Y, scaled by the factors of the LF dequantization. The restoration
    /// filters of the frame are applied before the samples are upsampled.
    /// </summary>
    private void AddModularColorChannels(JpegXlDecodedFrame decoded, JpegXlFrameHeader frame, JpegXlImageBuffer buffer, int codedWidth, int codedHeight, int upsampling)
    {
        var count = decoded.ModularColorChannelCount;
        var filter = frame.RestorationFilter;
        var isFiltered = filter.IsGaborEnabled || filter.EpfIterations != 0;
        if (isFiltered && count != 3)
            throw JpegXlFormat.Unsupported("The JPEG XL image has a grayscale Modular frame with restoration filters, which is not implemented yet.", "restoration filters on a grayscale Modular frame");

        var planes = new Av1PooledArray<float>?[count];
        try
        {
            var length = codedWidth * codedHeight;
            for (var c = 0; c < count; c++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                planes[c] = Av1PooledArray<float>.Rent(_scope, length, clear: false);
                var target = planes[c]!.Span[..length];
                if (_image.IsXybEncoded)
                {
                    var factor = decoded.LfQuant[c];
                    var source = decoded.Modular.Channels[c < 2 ? 1 - c : 2].Samples;
                    if (c == 2)
                    {
                        var luma = decoded.Modular.Channels[0].Samples;
                        for (var i = 0; i < length; i++)
                        {
                            target[i] = (source[i] + luma[i]) * factor;
                        }
                    }
                    else
                    {
                        for (var i = 0; i < length; i++)
                        {
                            target[i] = source[i] * factor;
                        }
                    }
                }
                else
                {
                    var scale = 1f / ((1 << _image.BitDepth.BitsPerSample) - 1);
                    var source = decoded.Modular.Channels[c].Samples;
                    for (var i = 0; i < length; i++)
                    {
                        target[i] = source[i] * scale;
                    }
                }
            }

            if (isFiltered)
            {
                ReadOnlySpan<Av1PooledArray<float>> filtered = [planes[0]!, planes[1]!, planes[2]!];
                if (filter.IsGaborEnabled)
                {
                    JpegXlRestorationFilters.ApplyGaborish(filtered, codedWidth, codedWidth, codedHeight, filter, _scope, _cancellationToken);
                }

                if (filter.EpfIterations > 0)
                {
                    var blocksWide = (codedWidth + 7) / 8;
                    var blocksHigh = (codedHeight + 7) / 8;
                    using var inverseSigma = Av1PooledArray<float>.Rent(_scope, (long)blocksWide * blocksHigh, clear: false);
                    var blocks = inverseSigma.Span[..(blocksWide * blocksHigh)];
                    blocks.Fill(JpegXlRestorationFilters.GetModularInverseSigma(filter));
                    JpegXlRestorationFilters.ApplyEdgePreservingFilter(filtered, codedWidth, codedWidth, codedHeight, filter, blocks, blocksWide, _scope, _cancellationToken);
                }
            }

            for (var c = 0; c < count; c++)
            {
                var plane = planes[c]!;
                planes[c] = null;
                AddColorPlane(buffer, c, plane, codedWidth, codedWidth, codedHeight, upsampling);
            }
        }
        finally
        {
            foreach (var plane in planes)
            {
                plane?.Dispose();
            }
        }
    }

    /// <summary>Converts a Modular channel to floating-point samples, upsampled to the size of the frame.</summary>
    private void AddModularChannel(JpegXlModularChannel source, JpegXlImageBuffer buffer, int channel, float scale, int upsampling)
    {
        if (upsampling == 1)
        {
            CopyScaled(source, buffer, channel, scale);
            return;
        }

        using var samples = Av1PooledArray<float>.Rent(_scope, (long)source.Width * source.Height, clear: false);
        var values = samples.Span[..(source.Width * source.Height)];
        var integers = source.Samples;
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = integers[i] * scale;
        }

        JpegXlUpsampler.Upsample(values, source.Width, source.Width, source.Height, upsampling, GetKernels(upsampling), buffer.GetRow(channel, 0, buffer.Height), buffer.Width, buffer.Height, _cancellationToken);
    }

    private float[] GetKernels(int factor)
    {
        var index = BitOperations.Log2((uint)factor) - 1;
        return _upsamplingKernels[index] ??= JpegXlUpsampler.CreateKernels(factor, _image.TransformData);
    }

    private static void CopyScaled(JpegXlModularChannel source, JpegXlImageBuffer buffer, int channel, float scale)
    {
        for (var y = 0; y < buffer.Height; y++)
        {
            var input = source.GetRow(y);
            var output = buffer.GetRow(channel, y);
            for (var x = 0; x < output.Length; x++)
            {
                output[x] = input[x] * scale;
            }
        }
    }

    /// <summary>
    /// Converts the color channels of a frame to the color encoding of the image, in place: from XYB, or from YCbCr. A
    /// grayscale image has its samples in the first channel afterwards.
    /// </summary>
    private void ApplyColorTransform(JpegXlImageBuffer buffer, JpegXlFrameHeader frame)
    {
        buffer.IsBeforeColorTransform = false;
        if (buffer.ColorChannelCount != 3)
            return;

        var isGray = _image.ColorEncoding.IsGray;
        for (var y = 0; y < buffer.Height; y++)
        {
            if ((y & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var first = buffer.GetRow(0, y);
            var second = buffer.GetRow(1, y);
            var third = buffer.GetRow(2, y);
            if (_outputColor is not null)
            {
                _outputColor.Apply(first, second, third);
            }
            else if (frame.IsYCbCr)
            {
                JpegXlColorTransform.YCbCrToRgb(first, second, third);
                if (isGray)
                {
                    second.CopyTo(first);
                }
            }
            else if (isGray)
            {
                // Three channels without a color transform: the gray samples are the second one
                second.CopyTo(first);
            }
        }
    }

    /// <summary>Places a frame on the image: the reference frames it is blended with fill the image, then the frame is blended over its rectangle.</summary>
    private JpegXlImageBuffer Blend(JpegXlImageBuffer frameBuffer, JpegXlFrameHeader frame)
    {
        var imageWidth = (int)_image.Width;
        var imageHeight = (int)_image.Height;
        var colorCount = OutputColorChannelCount;
        var channelCount = colorCount + _extraChannelCount;
        JpegXlImageBuffer? owned = null;
        try
        {
            var canvas = owned = new JpegXlImageBuffer(imageWidth, imageHeight, colorCount, _extraChannelCount);
            Span<int> sources = stackalloc int[channelCount];
            for (var c = 0; c < channelCount; c++)
            {
                // Each extra channel is blended with its own reference frame
                var source = c < colorCount ? frame.Blending.Source : frame.ExtraChannelBlending[c - colorCount].Source;
                sources[c] = source;
                canvas.Allocate(c, _scope);
                var reference = _references[source];
                if (reference is null)
                {
                    for (var y = 0; y < imageHeight; y++)
                    {
                        canvas.GetRow(c, y).Clear();
                    }

                    continue;
                }

                if (reference.IsBeforeColorTransform && c < colorCount && (_image.IsXybEncoded || reference.ColorChannelCount != colorCount))
                    throw JpegXlFormat.Invalid("a frame is blended with a reference frame that was kept before its color transform.");

                if (reference.Width < imageWidth || reference.Height < imageHeight)
                    throw JpegXlFormat.Invalid("a frame is blended with a reference frame that is smaller than the image.");

                var referenceChannel = c < colorCount ? c : reference.ColorChannelCount + (c - colorCount);
                for (var y = 0; y < imageHeight; y++)
                {
                    reference.GetRow(referenceChannel, y)[..imageWidth].CopyTo(canvas.GetRow(c, y));
                }
            }

            // The part of the frame that is on the image
            var left = Math.Max(0, frame.X0);
            var top = Math.Max(0, frame.Y0);
            var right = (int)Math.Min(imageWidth, (long)frame.X0 + frameBuffer.Width);
            var bottom = (int)Math.Min(imageHeight, (long)frame.Y0 + frameBuffer.Height);
            if (right <= left || bottom <= top)
            {
                owned = null;
                return canvas;
            }

            var width = right - left;
            using var scratch = Av1PooledArray<float>.Rent(_scope, (long)channelCount * width, clear: false);
            Span<JpegXlChannelBlending> blendings = stackalloc JpegXlChannelBlending[1 + _extraChannelCount];
            blendings[0] = JpegXlChannelBlending.FromFrame(frame.Blending);
            for (var i = 0; i < _extraChannelCount; i++)
            {
                blendings[1 + i] = JpegXlChannelBlending.FromFrame(frame.ExtraChannelBlending[i]);
            }

            for (var y = top; y < bottom; y++)
            {
                if ((y & 63) == 0)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                }

                JpegXlBlending.BlendRow(_image, canvas, left, y, frameBuffer, left - frame.X0, y - frame.Y0, width, colorCount, blendings, scratch.Span);
            }

            owned = null;
            return canvas;
        }
        finally
        {
            owned?.Dispose();
        }
    }
}
