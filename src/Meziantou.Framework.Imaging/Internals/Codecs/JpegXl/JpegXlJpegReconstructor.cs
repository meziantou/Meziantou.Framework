using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Rebuilds the JPEG file a JPEG XL file was made from (ISO/IEC 18181-2, annex A): the <c>jbrd</c> box describes the file,
/// the frame of the codestream holds its quantized coefficients and its quantization tables, and the ICC profile of the
/// codestream, the <c>Exif</c> box and the <c>xml </c> box hold the content of the application segments that had them.
/// </summary>
/// <remarks>
/// <para>
/// Every encoder writes the box before the codestream, and the frame is then decoded as it arrives. The box may also
/// follow the codestream: a frame that could be the one of a JPEG file (<see cref="IsJpegFrame"/>) is kept as it is
/// coded until the end of the container tells whether a box describes it. Any other frame without a box before it ends
/// the parsing: the file does not hold a JPEG file, and nothing after its first frame is read. A bare codestream has no
/// box, and nothing of its frames is read.
/// </para>
/// <para>
/// Nothing is approximated: a frame that cannot be the one of a recompressed JPEG file and that a box precedes, or a
/// description that does not match its frame, is an error.
/// </para>
/// </remarks>
internal sealed class JpegXlJpegReconstructor : JpegXlDecodeObserver
{
    private static ReadOnlySpan<byte> IccSignature => "ICC_PROFILE\0"u8;

    private static ReadOnlySpan<byte> ExifSignature => "Exif\0\0"u8;

    private static ReadOnlySpan<byte> XmpSignature => "http://ns.adobe.com/xap/1.0/\0"u8;

    private readonly ImageCodecContext _context;
    private JpegXlJpegReconstructionData? _data;
    private JpegCoefficientStore? _coefficients;
    private ushort[][]? _quantization;
    private JpegXlFrameHeader? _frame;
    private JpegBitstreamWriter? _writer;
    private byte[]? _exif;
    private byte[]? _xmp;
    private bool _frameSeen;

    // The frame that was read before any reconstruction data: its header, its table of contents and its sections
    private JpegXlImageHeader? _pendingImage;
    private JpegXlToc? _pendingToc;
    private WebPPayloadBuffer? _pendingSections;

    public JpegXlJpegReconstructor(ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Gets a value indicating whether the file holds JPEG reconstruction data, once it is parsed.</summary>
    public bool HasReconstructionData => _data is not null;

    /// <summary>Gets the bytes of the JPEG file, once the file is parsed and holds JPEG reconstruction data.</summary>
    public ReadOnlyMemory<byte> JpegFile => _writer?.WrittenMemory ?? throw new InvalidOperationException("The JPEG XL file holds no JPEG reconstruction data.");

    public override bool WantsFrames => _data is not null;

    public override bool WantsJpegReconstructionData => true;

    /// <summary>
    /// Gets a value indicating whether a frame can be the one of a recompressed JPEG file: the only frame of the image,
    /// with the three 8-bit channels of the file, coded with VarDCT, and nothing else.
    /// </summary>
    public static bool IsJpegFrame(JpegXlImageHeader header, JpegXlFrameHeader frame)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(frame);
        return !header.IsXybEncoded && header.ExtraChannels.Length == 0 && !header.HasAnimation && header.BitDepth.BitsPerSample == 8 && frame.Type == JpegXlFrameHeader.TypeRegular && frame.IsLast && !frame.IsModular;
    }

    public override bool EndsAtHeader(JpegXlStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return _data is null && !structure.IsContainer;
    }

    public override bool WantsFrame(JpegXlStructureParser structure, JpegXlFrameHeader frame)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(frame);

        // Without reconstruction data yet, the only frame worth keeping is one they could describe, when they follow it
        return _data is not null || (!_frameSeen && IsJpegFrame(structure.Header, frame));
    }

    public override void OnHeaderComplete(JpegXlStructureParser structure)
    {
    }

    public override void OnJpegReconstructionData(ReadOnlySpan<byte> payload)
    {
        if (_data is not null)
            throw JpegXlFormat.Invalid("the container has two boxes of JPEG reconstruction data.");

        _data = JpegXlJpegReconstructionData.Read(payload.ToArray(), payload.Length, _context.Tracker);
    }

    public override void OnMetadataBox(uint type, ReadOnlySpan<byte> payload)
    {
        if (type == JpegXlFormat.ExifBox)
        {
            // The application segment holds what follows the offset of the TIFF header
            _exif ??= payload.Length >= 4 ? payload[4..].ToArray() : [];
        }
        else
        {
            _xmp ??= payload.ToArray();
        }
    }

    public override bool OnFrame(JpegXlStructureParser structure, JpegXlFrameHeader frame, JpegXlToc toc, WebPPayloadBuffer sections)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(toc);
        ArgumentNullException.ThrowIfNull(sections);
        var header = structure.Header;
        var isJpegFrame = !_frameSeen && IsJpegFrame(header, frame);
        if (_data is null)
        {
            if (!isJpegFrame)
                return false;

            // The reconstruction data may follow the codestream: the frame is kept as it is coded, and the other boxes are read
            _frameSeen = true;
            _frame = frame;
            _pendingImage = header;
            _pendingToc = toc;
            _pendingSections = new WebPPayloadBuffer(_context.Scope, sections.Length);
            _pendingSections.Append(sections.Span);
            return true;
        }

        // The frame of a recompressed JPEG file: the only one, with the three channels of the file and nothing else
        if (!isJpegFrame)
            throw JpegXlFormat.Invalid("the image that the JPEG reconstruction data describe is not the one of a JPEG file.");

        _frameSeen = true;
        _frame = frame;
        _coefficients = JpegXlFrameDecoder.DecodeJpegCoefficients(header, frame, toc, sections.Data, out _quantization, _context.Scope, _context.CancellationToken);
        return true;
    }

    public override void OnEnd(JpegXlStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _frameSeen = true;
        if (_data is not { } data)
        {
            ReleasePendingFrame();
            return;
        }

        var frame = _frame ?? throw JpegXlFormat.Invalid("the image that the JPEG reconstruction data describe has no frame.");
        if (_pendingSections is { } pending)
        {
            // The frame that preceded the reconstruction data
            _coefficients = JpegXlFrameDecoder.DecodeJpegCoefficients(_pendingImage!, frame, _pendingToc!, pending.Data, out _quantization, _context.Scope, _context.CancellationToken);
            ReleasePendingFrame();
        }

        var model = data.Model;
        var header = structure.Header;
        model.Width = (int)header.Width;
        model.Height = (int)header.Height;

        // The channels of the frame are Cb, Y and Cr, or red, green and blue; a gray file only has the second one
        if ((data.ComponentKind is JpegXlJpegReconstructionData.ComponentsGray or JpegXlJpegReconstructionData.ComponentsYCbCr) != frame.IsYCbCr && data.ComponentKind != JpegXlJpegReconstructionData.ComponentsCustom)
            throw JpegXlFormat.Invalid("the components that the JPEG reconstruction data describe are not the ones of the frame.");

        ReadOnlySpan<int> channels = data.IsGray ? [1] : frame.IsYCbCr ? [1, 0, 2] : [0, 1, 2];
        ReadOnlySpan<int> modes = [frame.ChromaSubsampling.First, frame.ChromaSubsampling.Second, frame.ChromaSubsampling.Third];
        ReadOnlySpan<int> horizontalFactors = [1, 2, 2, 1];
        ReadOnlySpan<int> verticalFactors = [1, 2, 1, 2];
        for (var c = 0; c < model.Components.Count; c++)
        {
            var component = model.Components[c];
            component.HorizontalSampling = horizontalFactors[modes[channels[c]]];
            component.VerticalSampling = verticalFactors[modes[channels[c]]];

            // The values of the quantization table of the component; two components that share a table have the same ones
            var table = model.QuantizationTables[component.QuantizationTable];
            var values = _quantization![channels[c]];
            if (table.HasValues && !table.Values.AsSpan().SequenceEqual(values))
                throw JpegXlFormat.Invalid("two components share a quantization table and do not have the same quantization values.");

            values.CopyTo(table.Values, 0);
            table.HasValues = true;
        }

        if (!frame.IsYCbCr)
        {
            // The samples of a JPEG file are centered on zero, which the frame only undoes for YCbCr samples: the DC
            // coefficients of other samples hold the center, 128, which is 1024 once transformed and divided by their
            // quantization step
            for (var c = 0; c < model.Components.Count; c++)
            {
                var channel = channels[c];
                var offset = 1024 / _quantization![channel][0];
                for (var y = 0; y < _coefficients!.GetBlocksHigh(channel); y++)
                {
                    for (var x = 0; x < _coefficients.GetBlocksWide(channel); x++)
                    {
                        var block = _coefficients.GetBlock(channel, x, y);
                        block[0] = (short)(block[0] - offset);
                    }
                }
            }
        }

        CompleteApplicationSegments(data, structure.IccProfileBytes);
        _writer = new JpegBitstreamWriter(model, _coefficients!, channels, _context.Scope, _context.CancellationToken);
        _writer.Write();
        _coefficients!.Dispose();
        _coefficients = null;
    }

    public override Image GetResult() => throw new InvalidOperationException("The reconstruction of a JPEG file gives no image.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _coefficients?.Dispose();
            _coefficients = null;
            _writer?.Dispose();
            _writer = null;
            ReleasePendingFrame();
        }

        base.Dispose(disposing);
    }

    private void ReleasePendingFrame()
    {
        _pendingSections?.Dispose();
        _pendingSections = null;
        _pendingImage = null;
        _pendingToc = null;
    }

    /// <summary>Gives their content to the application segments whose content the codestream and the other boxes hold.</summary>
    private void CompleteApplicationSegments(JpegXlJpegReconstructionData data, byte[]? icc)
    {
        var model = data.Model;
        var iccCount = data.ApplicationSegments.Count(segment => segment.Kind == JpegXlJpegReconstructionData.SegmentIcc);
        var iccIndex = 0;
        var iccOffset = 0;
        for (var i = 0; i < data.ApplicationSegments.Count; i++)
        {
            var (kind, length) = data.ApplicationSegments[i];
            switch (kind)
            {
                case JpegXlJpegReconstructionData.SegmentIcc:
                {
                    // The parts of the profile, in order, each after its rank and the number of parts
                    var count = length - 2 - IccSignature.Length - 2;
                    if (icc is null || iccCount > 255 || count < 0 || count > icc.Length - iccOffset)
                        throw JpegXlFormat.Invalid("the JPEG reconstruction data refer to an ICC profile that the codestream does not hold.");

                    var segment = CreateSegment(0xE2, length, IccSignature, extra: 2);
                    segment[3 + IccSignature.Length] = (byte)++iccIndex;
                    segment[4 + IccSignature.Length] = (byte)iccCount;
                    icc.AsSpan(iccOffset, count).CopyTo(segment.AsSpan(5 + IccSignature.Length));
                    iccOffset += count;
                    model.ApplicationSegments[i] = segment;
                    break;
                }

                case JpegXlJpegReconstructionData.SegmentExif:
                    model.ApplicationSegments[i] = CreateSegment(0xE1, length, ExifSignature, _exif, "the EXIF data");
                    break;

                case JpegXlJpegReconstructionData.SegmentXmp:
                    model.ApplicationSegments[i] = CreateSegment(0xE1, length, XmpSignature, _xmp, "the XMP packet");
                    break;
            }
        }

        if (iccCount > 0 && iccOffset != icc!.Length)
            throw JpegXlFormat.Invalid("the JPEG reconstruction data do not refer to the whole ICC profile of the codestream.");
    }

    private static byte[] CreateSegment(byte marker, int length, ReadOnlySpan<byte> signature, byte[]? content, string name)
    {
        if (content is null || content.Length != length - 2 - signature.Length)
            throw JpegXlFormat.Invalid($"the JPEG reconstruction data refer to {name} of the container, which it does not hold with that size.");

        var segment = CreateSegment(marker, length, signature, extra: 0);
        content.CopyTo(segment.AsSpan(3 + signature.Length));
        return segment;
    }

    /// <summary>Creates an application segment: the second byte of its marker, its length, its signature.</summary>
    private static byte[] CreateSegment(byte marker, int length, ReadOnlySpan<byte> signature, int extra)
    {
        if (length < 2 + signature.Length + extra)
            throw JpegXlFormat.Invalid("an application segment of the JPEG reconstruction data is too short for its content.");

        var segment = new byte[length + 1];
        segment[0] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(1), (ushort)length);
        signature.CopyTo(segment.AsSpan(3));
        return segment;
    }
}
