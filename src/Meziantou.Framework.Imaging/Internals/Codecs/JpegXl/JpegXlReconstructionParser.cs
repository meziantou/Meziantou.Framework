namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Parses a JPEG XL file for <see cref="JpegXlJpegReconstructor"/>, which rebuilds the JPEG file it was made from.</summary>
internal sealed class JpegXlReconstructionParser : ImageParser<JpegXlJpegReconstructor>
{
    private readonly JpegXlStructureParser _structure;
    private readonly JpegXlJpegReconstructor _reconstructor;

    public JpegXlReconstructionParser(ImageCodecContext context)
        : base(ImageFormat.JpegXl)
    {
        _reconstructor = new JpegXlJpegReconstructor(context);
        _structure = new JpegXlStructureParser(context, StructureWalk.Decode, _reconstructor);
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        => _structure.Parse(buffer, isEndOfInput, out consumed);

    public override JpegXlJpegReconstructor GetResult() => _reconstructor;

    public override Exception CreateTruncatedException(long position) => _structure.CreateTruncatedException(position);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reconstructor.Dispose();
            _structure.Dispose();
        }

        base.Dispose(disposing);
    }
}
