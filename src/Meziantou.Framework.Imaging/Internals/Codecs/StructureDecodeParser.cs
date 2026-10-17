namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Adapts a container structure parser running in <see cref="StructureWalk.Decode"/> mode and its decode observer to the
/// <see cref="ImageParser{TResult}"/> contract of eager loads. The structure parser owns the container rules (chunk order,
/// CRCs, sub-blocks, markers, limits); the observer only turns payloads into pixels.
/// </summary>
internal sealed class StructureDecodeParser : ImageParser<Image>
{
    private readonly ImageParser<ImageInfo> _structure;
    private readonly IImageDecodeObserver _observer;

    public StructureDecodeParser(ImageParser<ImageInfo> structure, IImageDecodeObserver observer)
        : base(structure.Format)
    {
        _structure = structure;
        _observer = observer;
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        => _structure.Parse(buffer, isEndOfInput, out consumed);

    public override Image GetResult() => _observer.GetResult();

    public override Exception CreateTruncatedException(long position) => _structure.CreateTruncatedException(position);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _observer.Dispose();
            _structure.Dispose();
        }

        base.Dispose(disposing);
    }
}
