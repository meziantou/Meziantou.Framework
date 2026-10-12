namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Parses the start of a JPEG XL file, up to the first bytes of its first frame, and tells whether a <c>jbrd</c> box
/// precedes them: the data that rebuild the JPEG file the codestream was made from.
/// </summary>
internal sealed class JpegXlReconstructionProbe : ImageParser<bool>
{
    private readonly JpegXlStructureParser _structure;

    public JpegXlReconstructionProbe(ImageCodecContext context)
        : base(ImageFormat.JpegXl)
    {
        _structure = new JpegXlStructureParser(context, StructureWalk.Header);
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        => _structure.Parse(buffer, isEndOfInput, out consumed);

    public override bool GetResult() => _structure.HasJpegReconstructionData;

    public override Exception CreateTruncatedException(long position) => _structure.CreateTruncatedException(position);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _structure.Dispose();
        }

        base.Dispose(disposing);
    }
}
