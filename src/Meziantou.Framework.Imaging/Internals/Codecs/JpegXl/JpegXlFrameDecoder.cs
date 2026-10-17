using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the sections of one frame (ISO/IEC 18181-1, annex C and G): <c>LfGlobal</c>, the LF groups, <c>HfGlobal</c>
/// and the groups of each pass. A frame of a single group and a single pass stores them one after the other in one
/// section; otherwise each has its own section, located by the table of contents.
/// </summary>
internal sealed class JpegXlFrameDecoder
{
    // The number of quantization tables, which takes part in the numbering of the Modular sub-bitstreams
    private const int QuantTableCount = 17;

    private readonly JpegXlImageHeader _image;
    private readonly JpegXlFrameHeader _frame;
    private readonly JpegXlToc _toc;
    private readonly JpegXlImageBuffer? _lfFrame;
    private readonly byte[] _data;
    private readonly AllocationScope _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly JpegXlBitReader _reader = new();
    private bool _captureJpegCoefficients;
    private JpegCoefficientStore? _jpegCoefficients;
    private ushort[][]? _jpegQuantization;

    private JpegXlFrameDecoder(JpegXlImageHeader image, JpegXlFrameHeader frame, JpegXlToc toc, byte[] data, JpegXlImageBuffer? lfFrame, AllocationScope scope, CancellationToken cancellationToken)
    {
        _lfFrame = lfFrame;
        _image = image;
        _frame = frame;
        _toc = toc;
        _data = data;
        _scope = scope;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Gets a value indicating whether the decoder implements what a frame uses.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The frame uses a coding tool that is not implemented.</exception>
    public static void EnsureSupported(JpegXlFrameHeader frame)
    {
        if (frame.IsModular && frame.IsYCbCr && frame.ChromaSubsampling != default)
            throw JpegXlFormat.Unsupported("The JPEG XL image has a Modular frame stored as YCbCr with subsampled channels, which is not implemented yet.", "YCbCr Modular frame with subsampled channels");

        // Patches and splines are drawn before the frame is upsampled, on channels of one size
        if (frame.Upsampling != 1 && (frame.Flags & (JpegXlFrameHeader.FlagPatches | JpegXlFrameHeader.FlagSplines)) != 0 && frame.ExtraChannelUpsampling.Any(factor => factor != frame.Upsampling))
            throw JpegXlFormat.Unsupported("The JPEG XL image has an upsampled frame with patches or splines whose extra channels are not upsampled by the same factor, which is not implemented yet.", "upsampling with patches or splines and extra channels of another resolution");
    }

    /// <summary>Decodes a frame.</summary>
    /// <param name="image">The image header.</param>
    /// <param name="frame">The frame header.</param>
    /// <param name="toc">The table of contents of the frame.</param>
    /// <param name="data">The sections of the frame, one after the other from the start of the array.</param>
    /// <param name="lfFrame">The LF frame of the level above the one of the frame, when it was decoded: a frame may take its LF image from it.</param>
    /// <param name="scope">The scope of the samples and of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows and sections.</param>
    /// <returns>
    /// The samples of the frame. Its Modular channels are the color channels of a Modular frame (one for a grayscale image
    /// that has no color transform, otherwise three), then the extra channels.
    /// </returns>
    public static JpegXlDecodedFrame Decode(JpegXlImageHeader image, JpegXlFrameHeader frame, JpegXlToc toc, byte[] data, JpegXlImageBuffer? lfFrame, AllocationScope scope, CancellationToken cancellationToken)
    {
        EnsureSupported(frame);
        return new JpegXlFrameDecoder(image, frame, toc, data, lfFrame, scope, cancellationToken).Decode(frame.PassCount)!;
    }

    /// <summary>
    /// Decodes a frame of several sections from its first sections: <c>LfGlobal</c>, the LF groups, <c>HfGlobal</c> and the
    /// groups of its first passes. What the other passes hold is zero: the coefficients they add, and the Modular channels
    /// they code.
    /// </summary>
    /// <param name="image">The image header.</param>
    /// <param name="frame">The frame header.</param>
    /// <param name="toc">The table of contents of the frame.</param>
    /// <param name="data">The sections of the frame; only the ones that are decoded are read.</param>
    /// <param name="passCount">The number of passes to decode: fewer than the frame has.</param>
    /// <param name="lfFrame">The LF frame of the level above the one of the frame, when it was decoded.</param>
    /// <param name="scope">The scope of the samples and of the working memory.</param>
    /// <param name="cancellationToken">The token checked between rows and sections.</param>
    /// <returns>
    /// The samples, or <see langword="null"/> when they would not show the image: a Modular frame whose color channels are
    /// coded in the groups and are not squeezed. An alpha channel that is not decoded yet and is not squeezed is opaque.
    /// </returns>
    public static JpegXlDecodedFrame? DecodePartial(JpegXlImageHeader image, JpegXlFrameHeader frame, JpegXlToc toc, byte[] data, int passCount, JpegXlImageBuffer? lfFrame, AllocationScope scope, CancellationToken cancellationToken)
    {
        EnsureSupported(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(passCount, frame.PassCount);
        return new JpegXlFrameDecoder(image, frame, toc, data, lfFrame, scope, cancellationToken).Decode(passCount);
    }

    /// <summary>
    /// Reads the quantized coefficients of a frame that holds a recompressed JPEG file: a VarDCT frame of 8x8 DCTs whose
    /// quantization tables are stored by their values. No sample is computed.
    /// </summary>
    /// <param name="image">The image header.</param>
    /// <param name="frame">The frame header.</param>
    /// <param name="toc">The table of contents of the frame.</param>
    /// <param name="data">The sections of the frame.</param>
    /// <param name="quantization">Receives the quantization table of each channel, in the order of the rows of a JPEG block.</param>
    /// <param name="scope">The scope of the coefficients and of the working memory.</param>
    /// <param name="cancellationToken">The token checked between sections.</param>
    /// <returns>The coefficients of each channel; the caller disposes them.</returns>
    /// <exception cref="InvalidImageContentException">The frame does not hold the coefficients of a JPEG file.</exception>
    public static JpegCoefficientStore DecodeJpegCoefficients(JpegXlImageHeader image, JpegXlFrameHeader frame, JpegXlToc toc, byte[] data, out ushort[][] quantization, AllocationScope scope, CancellationToken cancellationToken)
    {
        EnsureSupported(frame);
        var decoder = new JpegXlFrameDecoder(image, frame, toc, data, lfFrame: null, scope, cancellationToken) { _captureJpegCoefficients = true };
        decoder.Decode(frame.PassCount)?.Dispose();
        quantization = decoder._jpegQuantization!;
        return decoder._jpegCoefficients!;
    }

    private JpegXlDecodedFrame? Decode(int passCount)
    {
        var frame = _frame;
        var groupDimension = (int)frame.GroupDimension;
        var lfGroupCount = (int)frame.LfGroupCount;
        var groupCount = (int)frame.GroupCount;
        JpegXlModularImage? owned = null;
        JpegXlDecodedFrame? ownedResult = null;
        JpegXlMaTree? globalTree = null;
        JpegXlVarDctDecoder? varDct = null;
        JpegXlPatchDictionary? patches = null;
        JpegXlSplines? splines = null;
        try
        {
            var modular = owned = new JpegXlModularImage(_image.BitDepth.BitsPerSample);
            varDct = frame.IsModular ? null : new JpegXlVarDctDecoder(_image, frame, _lfFrame, _scope, _cancellationToken, _captureJpegCoefficients);
            if (_captureJpegCoefficients && (varDct is null || (frame.Flags & (JpegXlFrameHeader.FlagNoise | JpegXlFrameHeader.FlagPatches | JpegXlFrameHeader.FlagSplines | JpegXlFrameHeader.FlagUseLfFrame)) != 0 || frame.Upsampling != 1 || !frame.IsFullFrame || frame.Width != _image.Width || frame.Height != _image.Height))
                throw JpegXlFormat.Invalid("the frame that the JPEG reconstruction data describe does not hold the coefficients of a JPEG file.");


            // LfGlobal: the parameters of the LF dequantization, what a VarDCT frame adds, then the global Modular data
            float[] lfQuant = [1f / 4096, 1f / 512, 1f / 256];
            BeginSection(0);
            if ((frame.Flags & JpegXlFrameHeader.FlagPatches) != 0)
            {
                var paddedWidth = (int)((frame.CodedWidth + 7) / 8 * 8);
                var paddedHeight = (int)((frame.CodedHeight + 7) / 8 * 8);
                patches = JpegXlPatchDictionary.Read(_reader, paddedWidth, paddedHeight, _image.ExtraChannels.Length, _scope);
            }

            if ((frame.Flags & JpegXlFrameHeader.FlagSplines) != 0)
            {
                splines = JpegXlSplines.Read(_reader, (long)frame.CodedWidth * frame.CodedHeight, _scope);
            }

            var noiseStrengths = (frame.Flags & JpegXlFrameHeader.FlagNoise) != 0 ? JpegXlNoise.ReadParameters(_reader) : null;

            if (varDct is not null)
            {
                varDct.ReadLfDequantization(_reader);
                varDct.ReadLfGlobal(_reader);
            }
            else if (!_reader.ReadBool())
            {
                for (var c = 0; c < 3; c++)
                {
                    lfQuant[c] = _reader.ReadF16() * (1f / 128);
                }
            }

            // A frame that has a color transform has three color channels, even for a grayscale image
            var width = (int)frame.CodedWidth;
            var height = (int)frame.CodedHeight;
            var hasColorTransform = _image.IsXybEncoded || frame.IsYCbCr;
            var colorChannelCount = _image.ColorEncoding.IsGray && !hasColorTransform ? 1 : 3;
            var modularColorChannelCount = frame.IsModular ? colorChannelCount : 0;
            for (var i = 0; i < modularColorChannelCount; i++)
            {
                modular.AddChannel(width, height, 0, 0);
            }

            // An extra channel has its own upsampling factor, at least the one of the color channels
            var upsamplingShift = BitOperations.Log2((uint)frame.Upsampling);
            for (var i = 0; i < _image.ExtraChannels.Length; i++)
            {
                var factor = (uint)frame.ExtraChannelUpsampling[i];
                var shift = BitOperations.Log2(factor) - upsamplingShift;
                modular.AddChannel((int)((frame.Width + factor - 1) / factor), (int)((frame.Height + factor - 1) / factor), shift, shift);
            }

            if (_reader.ReadBool())
            {
                var limit = Math.Min(1L << 22, 1024 + ((long)width * height * (colorChannelCount + _image.ExtraChannels.Length) / 16));
                globalTree = JpegXlMaTree.Read(_reader, _scope, limit);
            }

            JpegXlModularDecoder.Decode(_reader, modular, streamIndex: 0, globalTree, groupDimension, undoTransforms: false, _scope, _cancellationToken);
            EndSection();

            // What the passes that are not decoded hold is zero. Squeezed channels are then the image at a lower resolution;
            // other channels are not the image at all
            var isPartial = passCount < frame.PassCount;
            var isSqueezed = modular.Transforms.Exists(transform => transform.IsSqueeze);
            if (isPartial && !isSqueezed && modularColorChannelCount != 0 && (width > groupDimension || height > groupDimension))
                return null;

            // An LF group holds the LF coefficients of a VarDCT frame, the Modular channels that are subsampled by 8 or more
            // in both directions, then the HF metadata of a VarDCT frame
            var tree = globalTree;
            JpegXlVarDctDecoder.ModularReader readModular = (image, streamIndex) => JpegXlModularDecoder.Decode(_reader, image, streamIndex, tree, int.MaxValue, undoTransforms: true, _scope, _cancellationToken);
            var lfDimension = groupDimension * 8;
            for (var group = 0; group < lfGroupCount; group++)
            {
                BeginSection(1 + group);
                varDct?.ReadLfCoefficients(_reader, group, readModular);
                var x = (int)(group % frame.LfGroupColumns) * (long)lfDimension;
                var y = (int)(group / frame.LfGroupColumns) * (long)lfDimension;
                DecodeGroup(modular, globalTree, x, y, lfDimension, minShift: 3, maxShift: int.MaxValue, streamIndex: 1 + lfGroupCount + group);
                varDct?.ReadHfMetadata(_reader, group, readModular);
                EndSection();
            }

            // HfGlobal is empty for a Modular frame
            BeginSection(1 + lfGroupCount);
            if (varDct is not null)
            {
                if (!_captureJpegCoefficients)
                {
                    varDct.FinishLf();
                }

                varDct.ReadHfGlobal(_reader, (tableIndex, tableWidth, tableHeight) => ReadRawQuantTable(tree, tableIndex, tableWidth, tableHeight, lfGroupCount));
            }

            EndSection();

            // The passes of a group are decoded one after the other, so that only its coefficients are kept
            for (var group = 0; group < groupCount; group++)
            {
                varDct?.BeginGroup(group);
                for (var pass = 0; pass < passCount; pass++)
                {
                    var (minShift, maxShift) = frame.GetModularShiftBracket(pass);
                    BeginSection(2 + lfGroupCount + (pass * groupCount) + group);
                    varDct?.ReadGroupPass(_reader, pass);
                    var x = (int)(group % frame.GroupColumns) * (long)groupDimension;
                    var y = (int)(group / frame.GroupColumns) * (long)groupDimension;
                    var streamIndex = 1 + (3 * lfGroupCount) + QuantTableCount + (groupCount * pass) + group;
                    DecodeGroup(modular, globalTree, x, y, groupDimension, minShift, maxShift, streamIndex);
                    EndSection();
                }

                if (_captureJpegCoefficients)
                {
                    varDct!.CaptureJpegGroup();
                }
                else
                {
                    varDct?.FinishGroup();
                }
            }

            if (_captureJpegCoefficients)
            {
                // The coefficients are what the caller asked for: no sample is computed
                _jpegQuantization = varDct!.JpegQuantization;
                _jpegCoefficients = varDct.DetachJpegCoefficients();
                return null;
            }

            varDct?.UpsampleChroma();
            varDct?.ApplyRestorationFilters();

            JpegXlModularDecoder.UndoTransforms(modular, _scope);
            if (isPartial && !isSqueezed)
            {
                for (var i = 0; i < _image.ExtraChannels.Length; i++)
                {
                    var channel = modular.Channels.Count == modularColorChannelCount + _image.ExtraChannels.Length ? modular.Channels[modularColorChannelCount + i] : null;
                    if (channel is not null && _image.ExtraChannels[i].Type == JpegXlExtraChannelInfo.TypeAlpha && (channel.Width > groupDimension || channel.Height > groupDimension))
                    {
                        channel.Samples.Fill((1 << _image.ExtraChannels[i].BitDepth.BitsPerSample) - 1);
                    }
                }
            }

            if (modular.MetaChannelCount != 0 || modular.Channels.Count != modularColorChannelCount + _image.ExtraChannels.Length)
                throw JpegXlFormat.Invalid("the transforms of a frame do not give back the channels of the image.");

            for (var i = 0; i < modular.Channels.Count; i++)
            {
                var factor = i < modularColorChannelCount ? (uint)frame.Upsampling : (uint)frame.ExtraChannelUpsampling[i - modularColorChannelCount];
                if (modular.Channels[i].Width != (int)((frame.Width + factor - 1) / factor) || modular.Channels[i].Height != (int)((frame.Height + factor - 1) / factor))
                    throw JpegXlFormat.Invalid("the transforms of a frame do not give back the size of the image.");
            }

            var result = ownedResult = new JpegXlDecodedFrame(modular, modularColorChannelCount) { LfQuant = lfQuant };
            owned = null;
            result.Patches = patches;
            patches = null;
            result.Splines = splines;
            splines = null;
            result.NoiseStrengths = noiseStrengths;
            if (varDct is not null)
            {
                result.BaseCorrelationX = varDct.BaseCorrelationX;
                result.BaseCorrelationB = varDct.BaseCorrelationB;
            }

            if (varDct is not null)
            {
                for (var c = 0; c < 3; c++)
                {
                    var planeWidth = varDct.GetPlaneWidth(c);
                    var planeHeight = varDct.GetPlaneHeight(c);
                    result.SetPlane(c, varDct.DetachPlane(c), planeWidth, planeHeight, varDct.GetHorizontalShift(c), varDct.GetVerticalShift(c));
                }
            }

            ownedResult = null;
            return result;
        }
        finally
        {
            owned?.Dispose();
            ownedResult?.Dispose();
            patches?.Dispose();
            splines?.Dispose();
            globalTree?.Dispose();
            varDct?.Dispose();
        }
    }

    /// <summary>Reads the values of a quantization table that <c>HfGlobal</c> stores as a Modular image.</summary>
    private JpegXlModularImage ReadRawQuantTable(JpegXlMaTree? globalTree, int tableIndex, int width, int height, int lfGroupCount)
    {
        JpegXlModularImage? owned = null;
        try
        {
            var image = owned = new JpegXlModularImage(8);
            for (var i = 0; i < 3; i++)
            {
                image.AddChannel(width, height, 0, 0);
            }

            JpegXlModularDecoder.Decode(_reader, image, 1 + (3 * lfGroupCount) + tableIndex, globalTree, int.MaxValue, undoTransforms: true, _scope, _cancellationToken);
            owned = null;
            return image;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Decodes the part of the Modular image coded in one group: the channels larger than a group, of the resolutions of
    /// the group, restricted to its rectangle. They form an image with its own header and transforms.
    /// </summary>
    private void DecodeGroup(JpegXlModularImage full, JpegXlMaTree? globalTree, long x0, long y0, int dimension, int minShift, int maxShift, int streamIndex)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var groupDimension = (int)_frame.GroupDimension;
        var first = full.MetaChannelCount;
        while (first < full.Channels.Count && full.Channels[first].Width <= groupDimension && full.Channels[first].Height <= groupDimension)
        {
            first++;
        }

        using var group = new JpegXlModularImage(full.BitDepth);
        for (var i = first; i < full.Channels.Count; i++)
        {
            if (TryGetGroupRectangle(full.Channels[i], x0, y0, dimension, minShift, maxShift, out _, out _, out var width, out var height))
            {
                group.AddChannel(width, height, full.Channels[i].HorizontalShift, full.Channels[i].VerticalShift);
            }
        }

        if (group.Channels.Count == 0)
            return;

        JpegXlModularDecoder.Decode(_reader, group, streamIndex, globalTree, int.MaxValue, undoTransforms: true, _scope, _cancellationToken);
        var index = 0;
        for (var i = first; i < full.Channels.Count; i++)
        {
            var target = full.Channels[i];
            if (!TryGetGroupRectangle(target, x0, y0, dimension, minShift, maxShift, out var x, out var y, out var width, out var height))
                continue;

            if (index >= group.Channels.Count || group.Channels[index].Width != width || group.Channels[index].Height != height)
                throw JpegXlFormat.Invalid("the transforms of a group do not give back its channels.");

            var source = group.Channels[index++];
            for (var row = 0; row < height; row++)
            {
                source.GetRow(row).CopyTo(target.GetRow(y + row).Slice(x, width));
            }
        }

        if (index != group.Channels.Count)
            throw JpegXlFormat.Invalid("the transforms of a group do not give back its channels.");
    }

    /// <summary>
    /// Gets the rectangle of a channel that a group of the frame codes, when the group codes the resolution of the channel:
    /// the channels whose smaller shift is in the range of the group.
    /// </summary>
    internal static bool TryGetGroupRectangle(JpegXlModularChannel channel, long x0, long y0, int dimension, int minShift, int maxShift, out int x, out int y, out int width, out int height)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;
        var shift = Math.Min(channel.HorizontalShift, channel.VerticalShift);
        if (shift > maxShift || shift < minShift || channel.HorizontalShift < 0 || channel.VerticalShift < 0)
            return false;

        var left = x0 >> channel.HorizontalShift;
        var top = y0 >> channel.VerticalShift;
        if (left >= channel.Width || top >= channel.Height)
            return false;

        x = (int)left;
        y = (int)top;
        width = Math.Min(dimension >> channel.HorizontalShift, channel.Width - x);
        height = Math.Min(dimension >> channel.VerticalShift, channel.Height - y);
        return width > 0 && height > 0;
    }

    private void BeginSection(int index)
    {
        // A frame of a single section is one bit stream: its parts follow each other without alignment
        if (_toc.Sizes.Length == 1)
        {
            if (index == 0)
            {
                _reader.Reset(_data, 0, (int)_toc.Sizes[0]);
            }

            return;
        }

        _reader.Reset(_data, (int)_toc.Offsets[index], (int)_toc.Sizes[index]);
    }

    private void EndSection() => _reader.EnsureNotOverrun();
}
