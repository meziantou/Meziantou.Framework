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
/// The parser never consumes partial input: it asks for a growing contiguous window until the driver reports the end of
/// the input, so the bytes it finally sees are exactly the input, and the encoded-byte limit is enforced by the driver
/// before the window grows past it.
/// </para>
/// </remarks>
/// <typeparam name="TResult">The result type (<see cref="ImageInfo"/> or <see cref="Image"/>).</typeparam>
internal sealed class WholeInputParser<TResult> : ImageParser<TResult>
{
    private const int InitialRequest = 16 * 1024;

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
            consumed = 0;
            return ParseStatus.NeedMoreData(buffer.Length + Math.Max(InitialRequest, buffer.Length));
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
