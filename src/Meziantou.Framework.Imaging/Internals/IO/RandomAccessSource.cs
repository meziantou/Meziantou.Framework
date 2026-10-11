namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One operation's bounds-checked view of a <see cref="RandomAccessInput"/>: it validates every offset and length against
/// the input, charges the bytes actually read to <see cref="ImageResourceLimits.MaxEncodedBytes"/>, and rents its buffers
/// from the allocation scope of the operation.
/// </summary>
/// <remarks>
/// A malformed offset or count fails with <see cref="InvalidImageContentException"/> instead of reading out of range or
/// allocating an unbounded buffer, and a file whose offsets jump back and forth cannot make a decoder read an unbounded
/// number of bytes from a bounded file.
/// </remarks>
internal sealed class RandomAccessSource
{
    private readonly RandomAccessInput _input;
    private readonly ImageCodecContext _context;
    private readonly bool _chargeReads;
    private readonly long _origin;

    /// <summary>Creates a view of an input for one operation.</summary>
    /// <param name="input">The input.</param>
    /// <param name="context">The operation context.</param>
    /// <param name="format">The format reported in exceptions.</param>
    /// <param name="chargeReads">
    /// <see langword="false"/> when the bytes of the input were already charged to the encoded-byte limit (the whole-input
    /// buffering path of <see cref="WholeInputParser{TResult}"/>), so that they are not counted twice.
    /// </param>
    public RandomAccessSource(RandomAccessInput input, ImageCodecContext context, ImageFormat format, bool chargeReads = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        _input = input;
        _context = context;
        _chargeReads = chargeReads;
        Format = format;
        Length = input.Length;
    }

    private RandomAccessSource(RandomAccessSource parent, long offset, long length, ImageFormat format)
    {
        _input = parent._input;
        _context = parent._context;
        _chargeReads = parent._chargeReads;
        _origin = parent._origin + offset;
        Format = format;
        Length = length;
    }

    /// <summary>Gets the number of bytes of the input (of the range, for a view created by <see cref="Slice"/>).</summary>
    public long Length { get; }

    /// <summary>Gets the format reported in the exceptions of out-of-range offsets.</summary>
    public ImageFormat Format { get; }

    /// <summary>Gets the allocation scope of the operation.</summary>
    public AllocationScope Scope => _context.Scope;

    /// <summary>Gets the per-input limits of the operation.</summary>
    public ImageResourceLimits Limits => _context.Limits;

    /// <summary>Gets the context of the operation.</summary>
    public ImageCodecContext Context => _context;

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes at <paramref name="offset"/>.</summary>
    /// <param name="offset">The offset from the first byte of the input.</param>
    /// <param name="destination">The destination buffer, filled completely.</param>
    /// <param name="charge">
    /// <see langword="false"/> for bytes that another parser will charge itself (an icon payload handed to the PNG
    /// decoder), so that they are not counted twice.
    /// </param>
    /// <exception cref="InvalidImageContentException">The range is outside the input.</exception>
    /// <exception cref="ImageResourceLimitException">The cumulative number of bytes read exceeds <see cref="ImageResourceLimits.MaxEncodedBytes"/>.</exception>
    /// <exception cref="IOException">The stream failed (propagated unchanged).</exception>
    public void Read(long offset, Span<byte> destination, bool charge = true)
    {
        EnsureRange(offset, destination.Length);
        if (destination.IsEmpty)
            return;

        _context.CancellationToken.ThrowIfCancellationRequested();
        if (charge && _chargeReads)
        {
            _context.Tracker.ChargeEncodedBytes(destination.Length);
        }

        _input.Read(_origin + offset, destination);
    }

    /// <summary>
    /// Creates a view of a range of this input whose offsets start at zero: a file embedded in a container (an icon inside an
    /// animated cursor) is read by the code that reads it standalone, and none of its offsets can reach outside the range.
    /// The view shares the limits, the accounting and the allocation scope of this source.
    /// </summary>
    /// <param name="offset">The offset of the range from the first byte of this input.</param>
    /// <param name="length">The number of bytes of the range.</param>
    /// <param name="format">The format reported in the exceptions of out-of-range offsets of the view.</param>
    /// <returns>The view.</returns>
    /// <exception cref="InvalidImageContentException">The range is outside the input.</exception>
    public RandomAccessSource Slice(long offset, long length, ImageFormat format)
    {
        EnsureRange(offset, length);
        return new RandomAccessSource(this, offset, length, format);
    }

    /// <summary>Reads a range into a buffer rented from the allocation scope.</summary>
    /// <param name="offset">The offset from the first byte of the input.</param>
    /// <param name="length">The number of bytes; it must fit in one buffer.</param>
    /// <param name="charge"><see langword="false"/> for bytes another parser will charge itself.</param>
    /// <returns>The buffer, exactly <paramref name="length"/> bytes long. The caller disposes it.</returns>
    /// <exception cref="InvalidImageContentException">The range is outside the input.</exception>
    /// <exception cref="ImageResourceLimitException">The range is longer than one buffer, or a limit is exceeded.</exception>
    public PooledBuffer ReadToBuffer(long offset, long length, bool charge = true)
    {
        EnsureRange(offset, length);
        if (length > CheckedSizes.MaxBufferLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, CheckedSizes.MaxBufferLength, length);

        var buffer = _context.Scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
        try
        {
            Read(offset, buffer.Span, charge);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    /// <summary>Determines whether a range is entirely inside the input.</summary>
    /// <param name="offset">The offset from the first byte of the input.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns><see langword="true"/> if the range is valid.</returns>
    public bool IsInRange(long offset, long length) => offset >= 0 && length >= 0 && length <= Length - offset;

    /// <summary>Throws when a range is not entirely inside the input.</summary>
    /// <param name="offset">The offset from the first byte of the input.</param>
    /// <param name="length">The number of bytes.</param>
    /// <exception cref="InvalidImageContentException">The range is outside the input.</exception>
    public void EnsureRange(long offset, long length)
    {
        if (!IsInRange(offset, length))
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The {ImageFormatNames.Get(Format)} data references bytes {offset} to {(offset < 0 || length < 0 ? offset : offset + length)} of a {Length}-byte input."), Format);
    }
}
