namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Drives the eager decode parser of a codec (<see cref="ImageCodec.CreateDecodeParser"/>) one step at a time for a
/// sequential reader: each <see cref="ImageInputPump"/> run returns at the next <see cref="SequentialDecodeEvent"/> (header,
/// completed image, or clean end of input). The decoder, container rules and limits are exactly those of eager loads.
/// </summary>
/// <remarks>
/// A step result reported by the session wins over the status of the inner parser: the walker may have consumed bytes and
/// returned at a yield point (<see cref="ParseStatus.Complete"/> without being done), or even asked for more data. Once the
/// inner parser completes without a pending result, the input ended cleanly (the walker validated the end of the container,
/// or stopped at the frame limit): every later step reports <see cref="SequentialDecodeEvent.End"/> again.
/// </remarks>
internal sealed class SequentialDecodeParser : ImageParser<SequentialDecodeEvent>
{
    private readonly ImageParser<Image> _inner;
    private readonly SequentialDecodeSession _session;
    private SequentialDecodeEvent _result;
    private bool _ended;

    public SequentialDecodeParser(ImageParser<Image> inner, SequentialDecodeSession session)
        : base(inner.Format)
    {
        _inner = inner;
        _session = session;
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        if (_ended)
        {
            consumed = 0;
            _result = SequentialDecodeEvent.End;
            return ParseStatus.Complete;
        }

        var status = _inner.Parse(buffer, isEndOfInput, out consumed);
        if (_session.TryTakeEvent(out var result))
        {
            _result = result;
            return ParseStatus.Complete;
        }

        if (status.IsComplete)
        {
            _ended = true;
            _result = SequentialDecodeEvent.End;
        }

        return status;
    }

    public override SequentialDecodeEvent GetResult() => _result;

    public override Exception CreateTruncatedException(long position) => _inner.CreateTruncatedException(position);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _session.ReleasePendingImage();
        }

        base.Dispose(disposing);
    }
}
