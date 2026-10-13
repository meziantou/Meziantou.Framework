using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the JPEG XL file that holds a JPEG file (ISO/IEC 18181-2, annex A): the container, with the <c>jbrd</c> box that
/// describes the file, the <c>Exif</c> and <c>xml </c> boxes of its EXIF data and of its XMP packet, and a codestream
/// whose only frame holds the quantized coefficients of the file. No pixel is decoded.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The file is read by <see cref="JpegBitstreamReader"/>, then written back by <see cref="JpegBitstreamWriter"/> and
/// compared with the input: a file the writer does not write back byte for byte is not recompressed.
/// </description></item>
/// <item><description>
/// The output is then read by <see cref="JpegXlJpegReconstructor"/> and compared with the input, so that a file this
/// class writes always gives back the JPEG file.
/// </description></item>
/// <item><description>
/// The ICC profile of the file is the one of the codestream; its application segments are rebuilt from it when they hold
/// its parts in order, and stored as they are otherwise. The first EXIF segment and the first XMP segment are stored in
/// their boxes.
/// </description></item>
/// </list>
/// </remarks>
internal static class JpegXlJpegRecompressor
{
    private static ReadOnlySpan<byte> IccSignature => "ICC_PROFILE\0"u8;

    private static ReadOnlySpan<byte> ExifSignature => "Exif\0\0"u8;

    private static ReadOnlySpan<byte> XmpSignature => "http://ns.adobe.com/xap/1.0/\0"u8;

    /// <summary>Recompresses a JPEG file.</summary>
    /// <param name="jpeg">The whole JPEG file.</param>
    /// <param name="effort">The compression effort, from 0 to 9.</param>
    /// <param name="context">The context of the operation.</param>
    /// <returns>The JPEG XL file.</returns>
    public static byte[] Recompress(ReadOnlySpan<byte> jpeg, int effort, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scope = context.Scope;
        var cancellationToken = context.CancellationToken;

        // What the decoder of the library says of the file: the errors of a file it does not read are the same ones, and
        // the metadata of the JPEG XL image are the ones it reports
        var info = ImageIO.Identify(jpeg, new ImageIdentifyOptions { Configuration = context.Configuration, Mode = ImageIdentifyMode.FullScan });
        var model = JpegBitstreamReader.Read(jpeg, scope, cancellationToken, out var coefficients);
        using (coefficients)
        {
            Span<int> identity = [0, 1, 2];
            using (var check = new JpegBitstreamWriter(model, coefficients, identity[..model.Components.Count], scope, cancellationToken))
            {
                check.Write();
                if (!check.WrittenMemory.Span.SequenceEqual(jpeg))
                    throw new UnsupportedImageFeatureException("The JPEG file cannot be recompressed: its entropy-coded data are not coded as a JPEG XL file can describe them.", ImageFormat.Jpeg, "JPEG recompression: entropy coding that cannot be rebuilt");
            }

            var result = Write(model, coefficients, info, effort, context);

            // What was written gives back the file
            var verification = new ImageCodecContext(context.Configuration, "JpegXlTranscoder", cancellationToken);
            using var parser = new JpegXlReconstructionParser(verification);
            var reconstructor = ImageInputPump.Run(result, parser, verification);
            if (!reconstructor.HasReconstructionData || !reconstructor.JpegFile.Span.SequenceEqual(jpeg))
                throw new UnsupportedImageFeatureException("The JPEG file cannot be recompressed: the JPEG XL file that holds it does not give it back.", ImageFormat.Jpeg, "JPEG recompression: file that is not rebuilt");

            return result;
        }
    }

    private static byte[] Write(JpegBitstreamModel model, JpegCoefficientStore coefficients, ImageInfo info, int effort, ImageCodecContext context)
    {
        var scope = context.Scope;
        var componentCount = model.Components.Count;
        var isGray = componentCount == 1;
        var isYCbCr = isGray || IsYCbCr(model);

        // The channels of the frame: Cb, Y and Cr, or red, green and blue; a grayscale file only has Y
        ReadOnlySpan<int> channelComponents = isGray ? [-1, 0, -1] : isYCbCr ? [1, 0, 2] : [0, 1, 2];
        Span<int> modes = stackalloc int[3];
        var quantization = new ushort[3][];
        Span<int> dcOffset = stackalloc int[3];
        for (var c = 0; c < 3; c++)
        {
            var component = model.Components[Math.Max(channelComponents[c], 0)];
            modes[c] = (component.HorizontalSampling, component.VerticalSampling) switch
            {
                (1, 1) => 0,
                (2, 2) => 1,
                (2, 1) => 2,
                _ => 3,
            };
            quantization[c] = model.QuantizationTables[component.QuantizationTable].Values;
            if (!isYCbCr)
            {
                // The samples of a JPEG file are centered on zero, which a decoder of JPEG XL only undoes for YCbCr samples:
                // the DC coefficients hold the center, 128, which is 1024 once transformed, in quantization steps
                if (modes[c] != 0)
                    throw new UnsupportedImageFeatureException("The JPEG file cannot be recompressed: its components are red, green and blue, and are subsampled.", ImageFormat.Jpeg, "JPEG recompression: subsampled RGB components");

                dcOffset[c] = 1024 / quantization[c][0];
            }
        }

        // The profile of the image, when it describes its components
        byte[]? icc = null;
        if (info.Metadata.IccProfile is { } profile && profile.ColorSpace == (isGray ? IccProfileColorSpace.Gray : IccProfileColorSpace.Rgb))
        {
            icc = profile.Data.ToArray();
        }

        var segmentKinds = ClassifySegments(model, icc, out var exif, out var xmp);
        var jbrd = JpegXlJpegReconstructionData.Write(model, segmentKinds, brotliQuality: Math.Clamp(effort + 2, 0, 11), scope);

        // The codestream: the image header, the profile, then the frame
        var settings = new JpegXlFrameSettings
        {
            Flags = JpegXlFrameHeader.FlagSkipAdaptiveLfSmoothing,
            IsYCbCr = isYCbCr,
            ChromaSubsampling = (modes[0], modes[1], modes[2]),
        };
        var size = new Size(model.Width, model.Height);
        var color = icc is not null ? JpegXlEncoderColor.IccProfile : JpegXlEncoderColor.Srgb;
        using var codestream = new LsbBitWriter(scope);
        JpegXlHeaderWriter.WriteImageHeader(codestream, size, isGray, bitsPerSample: 8, hasAlpha: false, info.Metadata.Orientation, animation: null, color);
        JpegXlImageHeader imageHeader;
        using (var copy = new LsbBitWriter(scope))
        {
            // The headers are parsed back by the reader of the decoder: the geometry of the frame is the one it derives
            JpegXlHeaderWriter.WriteImageHeader(copy, size, isGray, bitsPerSample: 8, hasAlpha: false, info.Metadata.Orientation, animation: null, color);
            copy.ZeroPadToByte();
            var bytes = copy.WrittenSpan.ToArray();
            imageHeader = JpegXlImageHeader.Read(new JpegXlBitReader(bytes, 0, bytes.Length));
        }

        if (icc is not null)
        {
            JpegXlIccEncoder.Write(codestream, icc, scope);
        }

        codestream.ZeroPadToByte();
        JpegXlFrameHeader frameHeader;
        using (var copy = new LsbBitWriter(scope))
        {
            JpegXlHeaderWriter.WriteFrameHeader(copy, settings, isXybEncoded: false, hasAlpha: false, duration: null, isLast: true);
            copy.ZeroPadToByte();
            var bytes = copy.WrittenSpan.ToArray();
            frameHeader = JpegXlFrameHeader.Read(new JpegXlBitReader(bytes, 0, bytes.Length), imageHeader, isPreview: false);
        }

        using var sections = new LsbBitWriter(scope);
        var sizes = new List<int>();
        using (var encoder = new JpegXlVarDctEncoder(imageHeader, frameHeader, PixelFormat.Rgb24, quality: -1, effort, scope, context.CancellationToken))
        {
            encoder.EncodeJpegCoefficients(coefficients, channelComponents, quantization, dcOffset, sections, sizes);
        }

        JpegXlHeaderWriter.WriteFrameHeader(codestream, settings, isXybEncoded: false, hasAlpha: false, duration: null, isLast: true);
        JpegXlHeaderWriter.WriteToc(codestream, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(sizes));

        // The container: the description of the file precedes the codestream, so that a reader knows what the frame is
        // before it reads it
        using var output = new MemoryStream();
        output.Write(JpegXlFormat.ContainerSignature);
        WriteBox(output, JpegXlFormat.FileTypeBox, "jxl \0\0\0\0jxl "u8, default);
        WriteBox(output, JpegXlFormat.JpegReconstructionBox, jbrd, default);
        if (exif is not null)
        {
            // The payload starts with the offset of the TIFF header: none
            Span<byte> offset = [0, 0, 0, 0];
            WriteBox(output, JpegXlFormat.ExifBox, offset, exif);
        }

        if (xmp is not null)
        {
            WriteBox(output, JpegXlFormat.XmlBox, xmp, default);
        }

        WriteBox(output, JpegXlFormat.CodestreamBox, codestream.WrittenSpan, sections.WrittenSpan);
        return output.ToArray();
    }

    /// <summary>Tells whether the three components of a file are luma and chroma, as the decoder of the library decides it.</summary>
    private static bool IsYCbCr(JpegBitstreamModel model)
    {
        var hasJfif = false;
        foreach (var segment in model.ApplicationSegments)
        {
            var payload = segment.AsSpan(3);
            if (segment[0] == 0xEE && payload.Length >= 12 && payload.StartsWith("Adobe"u8))
                return payload[11] != 0;

            hasJfif |= segment[0] == 0xE0 && payload.StartsWith("JFIF\0"u8);
        }

        return hasJfif || !(model.Components[0].Id == 'R' && model.Components[1].Id == 'G' && model.Components[2].Id == 'B');
    }

    /// <summary>
    /// Finds the application segments whose content the JPEG XL file holds elsewhere: the ones that hold the parts of the
    /// ICC profile, in order, the first one that holds EXIF data, and the first one that holds an XMP packet.
    /// </summary>
    private static int[] ClassifySegments(JpegBitstreamModel model, byte[]? icc, out byte[]? exif, out byte[]? xmp)
    {
        var kinds = new int[model.ApplicationSegments.Count];
        exif = null;
        xmp = null;

        // The parts of the profile: numbered from 1, each with their number, and holding the profile once joined
        var iccSegments = new List<int>();
        for (var i = 0; i < kinds.Length; i++)
        {
            var segment = model.ApplicationSegments[i];
            var payload = segment.AsSpan(3);
            if (segment[0] == 0xE2 && payload.Length >= IccSignature.Length + 2 && payload.StartsWith(IccSignature))
            {
                iccSegments.Add(i);
            }
            else if (segment[0] == 0xE1 && exif is null && payload.StartsWith(ExifSignature))
            {
                exif = payload[ExifSignature.Length..].ToArray();
                kinds[i] = JpegXlJpegReconstructionData.SegmentExif;
            }
            else if (segment[0] == 0xE1 && xmp is null && payload.StartsWith(XmpSignature))
            {
                xmp = payload[XmpSignature.Length..].ToArray();
                kinds[i] = JpegXlJpegReconstructionData.SegmentXmp;
            }
        }

        if (icc is not null && iccSegments.Count is > 0 and <= 255)
        {
            var offset = 0;
            var isInOrder = true;
            for (var i = 0; i < iccSegments.Count && isInOrder; i++)
            {
                var payload = model.ApplicationSegments[iccSegments[i]].AsSpan(3 + IccSignature.Length);
                var part = payload[2..];
                isInOrder = payload[0] == i + 1 && payload[1] == iccSegments.Count && part.Length <= icc.Length - offset && part.SequenceEqual(icc.AsSpan(offset, part.Length));
                offset += part.Length;
            }

            if (isInOrder && offset == icc.Length)
            {
                foreach (var index in iccSegments)
                {
                    kinds[index] = JpegXlJpegReconstructionData.SegmentIcc;
                }
            }
        }

        return kinds;
    }

    private static void WriteBox(MemoryStream output, uint type, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var size = (long)JpegXlFormat.BoxHeaderLength + first.Length + second.Length;
        if (size > uint.MaxValue)
            throw new UnsupportedImageFeatureException("A box of the JPEG XL output is larger than 4 GiB.", ImageFormat.JpegXl, "JPEG XL box size");

        Span<byte> header = stackalloc byte[JpegXlFormat.BoxHeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], type);
        output.Write(header);
        output.Write(first);
        output.Write(second);
    }
}
