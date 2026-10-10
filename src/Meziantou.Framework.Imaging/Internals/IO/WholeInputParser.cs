namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bridge between the forward-only input pipeline and the random-access container formats (TIFF, BigTIFF, ICO, CUR):
/// it buffers the whole input, then runs the random-access decoder over the buffered bytes.
/// </summary>
/// <remarks>
/// <para>
/// This is how <c>Image.Load</c> and <c>Image.Identify</c> read these formats from a span, from a non-seekable stream, or
/// asynchronously. It is bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/> like any other input, and the
/// buffering is documented per format, never silent. The synchronous path over a seekable stream or a file does not use
/// it: <c>ImageIO</c> reads those through a <see cref="RandomAccessInput"/> directly, and
/// <see cref="ImageCollection"/> always does.
/// </para>
/// <para>
/// The parser never consumes partial input: it asks for one more contiguous byte, or the end of the input
/// (<see cref="ParseStatus.NeedMoreDataOrEnd"/>), until the driver reports the end, so the bytes it finally sees are exactly
/// the input. The driver reads as much as its buffer holds and doubles it when it is full, and it enforces the
/// encoded-byte limit: an input as long as the limit is buffered whole, a longer one fails with the limit, from a span and
/// from a stream alike.
/// </para>
/// </remarks>
/// <typeparam name="TResult">The result type (<see cref="ImageInfo"/> or <see cref="Image"/>).</typeparam>
internal sealed class WholeInputParser<TResult> : ImageParser<TResult>
{
    private readonly ImageCodecContext _context;
    private readonly Func<RandomAccessSource, TResult> _decode;
    private PooledBuffer? _buffer;
    private TResult? _result;
    private bool _completed;

    public WholeInputParser(ImageCodecContext context, ImageFormat format, Func<RandomAccessSource, TResult> decode)
        : base(format)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(decode);
        _context = context;
        _decode = decode;
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        if (!isEndOfInput)
        {
            // One more byte than the largest buffer cannot be buffered at all
            if (buffer.Length >= CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_context.Limits);

            consumed = 0;
            return ParseStatus.NeedMoreDataOrEnd(buffer.Length + 1);
        }

        consumed = buffer.Length;
        _buffer = _context.Scope.Rent(buffer.Length, AllocationKind.DecoderState, clear: false);
        buffer.CopyTo(_buffer.Span);

        // The driver already charged these bytes to the encoded-byte limit when it consumed them
        using var input = RandomAccessInput.Create(_buffer.Memory);
        _result = _decode(new RandomAccessSource(input, _context, Format, chargeReads: false));
        _completed = true;
        return ParseStatus.Complete;
    }

    public override TResult GetResult() => _completed ? _result! : throw new InvalidOperationException($"The {ImageFormatNames.Get(Format)} data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }

        base.Dispose(disposing);
    }
}
