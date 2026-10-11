namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Walks a JPEG XL codestream that is fed in arbitrary slices (a bare codestream, or the payloads of the codestream boxes
/// of a container): the image header and its ICC profile, then for each frame its header, its table of contents and its
/// sections, which are buffered for a decoder or skipped.
/// </summary>
/// <remarks>
/// <para>
/// The headers are bit-packed and have no length field: they are parsed on the bytes received so far, with a reader that
/// reads zeros past the end, and the attempt is discarded when it read past the end. Nothing is reported, charged or
/// checked against a limit before an attempt succeeds.
/// </para>
/// <para>
/// <see cref="Feed"/> stops at each <see cref="JpegXlCodestreamEvent"/> so that the caller can react to it; it takes every
/// byte it is given while the length of what it parses is unknown, and exactly the bytes of a frame once its table of
/// contents is read.
/// </para>
/// </remarks>
internal sealed class JpegXlCodestreamWalker : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly InputResourceTracker _tracker;
    private readonly JpegXlCodestreamBuffer _pending;
    private readonly JpegXlBitReader _reader = new();
    private Phase _phase;
    private bool _previewPending;
    private bool _attemptNeeded;
    private long _payloadRemaining;
    private bool _bufferPayload;

    public JpegXlCodestreamWalker(AllocationScope scope, InputResourceTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(tracker);
        _scope = scope;
        _tracker = tracker;
        _pending = new JpegXlCodestreamBuffer(scope);
    }

    private enum Phase
    {
        ImageHeader,
        FrameHeader,
        FramePayload,
        End,
    }

    /// <summary>Gets the image header, once <see cref="JpegXlCodestreamEvent.ImageHeader"/> was reported.</summary>
    public JpegXlImageHeader? ImageHeader { get; private set; }

    /// <summary>Gets the decoded ICC profile, when the color encoding announces one.</summary>
    public byte[]? IccProfile { get; private set; }

    /// <summary>Gets the header of the current frame, from <see cref="JpegXlCodestreamEvent.FrameHeader"/> to the next one.</summary>
    public JpegXlFrameHeader? FrameHeader { get; private set; }

    /// <summary>Gets the table of contents of the current frame.</summary>
    public JpegXlToc? Toc { get; private set; }

    /// <summary>Gets a value indicating whether the current frame is the preview frame.</summary>
    public bool IsPreviewFrame { get; private set; }

    /// <summary>Gets the sections of the current frame, when they are buffered, once <see cref="JpegXlCodestreamEvent.FrameComplete"/> was reported.</summary>
    public WebPPayloadBuffer? Payload { get; private set; }

    /// <summary>Gets a value indicating whether the last frame was walked: no byte of the codestream is expected any more.</summary>
    public bool IsComplete => _phase == Phase.End;

    /// <summary>Gets a value indicating whether bytes that were received are waiting to be parsed.</summary>
    public bool HasPendingBytes => _pending.Length > 0;

    /// <summary>
    /// Chooses what to do with the sections of the frame whose header was just reported: keep them for a decoder, or skip
    /// them. Must be called after each <see cref="JpegXlCodestreamEvent.FrameHeader"/>.
    /// </summary>
    public void BeginPayload(bool buffer)
    {
        if (_phase != Phase.FramePayload || Toc is null)
            throw new InvalidOperationException("No frame header was reported.");

        _bufferPayload = buffer;
        Payload?.Dispose();
        Payload = buffer ? new WebPPayloadBuffer(_scope, Toc.TotalLength) : null;
    }

    /// <summary>Releases the sections of the current frame.</summary>
    public void ReleasePayload()
    {
        Payload?.Dispose();
        Payload = null;
    }

    /// <summary>Gives bytes of the codestream to the walker.</summary>
    /// <param name="data">The next bytes of the codestream; may be empty to continue with the bytes already received.</param>
    /// <param name="consumed">The number of bytes of <paramref name="data"/> the walker took.</param>
    /// <returns>What the walker reached, or <see cref="JpegXlCodestreamEvent.None"/> when it needs more bytes.</returns>
    public JpegXlCodestreamEvent Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        consumed = 0;
        switch (_phase)
        {
            case Phase.ImageHeader:
                Take(data, ref consumed);
                if (!TryParseImageHeader())
                    return JpegXlCodestreamEvent.None;

                _phase = Phase.FrameHeader;
                _previewPending = ImageHeader!.PreviewSize is not null;
                _attemptNeeded = _pending.Length > 0;
                return JpegXlCodestreamEvent.ImageHeader;

            case Phase.FrameHeader:
                Take(data, ref consumed);
                if (!TryParseFrameHeader())
                    return JpegXlCodestreamEvent.None;

                _phase = Phase.FramePayload;
                _payloadRemaining = Toc!.TotalLength;
                return JpegXlCodestreamEvent.FrameHeader;

            case Phase.FramePayload:
                return FeedPayload(data, ref consumed);

            default:
                return JpegXlCodestreamEvent.None;
        }
    }

    public void Dispose()
    {
        Payload?.Dispose();
        Payload = null;
        _pending.Dispose();
    }

    private void Take(ReadOnlySpan<byte> data, ref int consumed)
    {
        if (data.IsEmpty)
            return;

        _pending.Append(data);
        consumed = data.Length;
        _attemptNeeded = true;
    }

    private JpegXlCodestreamEvent FeedPayload(ReadOnlySpan<byte> data, ref int consumed)
    {
        // The bytes received with the headers come first
        if (_pending.Length > 0 && _payloadRemaining > 0)
        {
            var count = (int)Math.Min(_pending.Length, _payloadRemaining);
            if (_bufferPayload)
            {
                Payload!.Append(_pending.Span[..count]);
            }

            _pending.Discard(count);
            _payloadRemaining -= count;
        }

        if (_payloadRemaining > 0 && !data.IsEmpty)
        {
            var count = (int)Math.Min(data.Length, _payloadRemaining);
            if (_bufferPayload)
            {
                Payload!.Append(data[..count]);
            }

            consumed = count;
            _payloadRemaining -= count;
        }

        if (_payloadRemaining > 0)
            return JpegXlCodestreamEvent.None;

        _phase = !IsPreviewFrame && FrameHeader!.IsLast ? Phase.End : Phase.FrameHeader;
        _attemptNeeded = _pending.Length > 0;
        return JpegXlCodestreamEvent.FrameComplete;
    }

    private bool TryParseImageHeader()
    {
        if (!_attemptNeeded)
            return false;

        _attemptNeeded = false;
        _reader.Reset(_pending.Data, 0, _pending.Length);
        JpegXlImageHeader header;
        byte[]? icc = null;
        try
        {
            header = JpegXlImageHeader.Read(_reader);
            if (header.ColorEncoding.WantIcc)
            {
                var limits = _tracker.Limits;
                icc = JpegXlIccDecoder.Read(_reader, _scope, limits.MaxMetadataBytes - _tracker.MetadataBytes, limits);
            }

            _reader.ZeroPadToByte();
        }
        catch (ImageException) when (_reader.IsOverrun)
        {
            // The bits that are missing were read as zeros: what failed is not what the file says
            return false;
        }

        if (_reader.IsOverrun)
            return false;

        if (icc is not null)
        {
            _tracker.ChargeMetadataBytes(icc.Length);
        }

        ImageHeader = header;
        IccProfile = icc;
        _pending.Discard((int)_reader.BytesConsumed);
        return true;
    }

    private bool TryParseFrameHeader()
    {
        if (!_attemptNeeded)
            return false;

        _attemptNeeded = false;
        _reader.Reset(_pending.Data, 0, _pending.Length);
        JpegXlFrameHeader header;
        JpegXlToc toc;
        try
        {
            header = JpegXlFrameHeader.Read(_reader, ImageHeader!, _previewPending);
            toc = JpegXlToc.Read(_reader, header.SectionCount, _scope);
        }
        catch (ImageException) when (_reader.IsOverrun)
        {
            return false;
        }

        if (_reader.IsOverrun)
            return false;

        IsPreviewFrame = _previewPending;
        _previewPending = false;
        FrameHeader = header;
        Toc = toc;
        ReleasePayload();
        _pending.Discard((int)_reader.BytesConsumed);
        return true;
    }
}
