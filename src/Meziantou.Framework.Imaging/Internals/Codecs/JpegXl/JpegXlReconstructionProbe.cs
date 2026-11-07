namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Parses a JPEG XL file as far as it takes to tell whether <see cref="JpegXlJpegReconstructor"/> would find the data that
/// rebuild a JPEG file (a <c>jbrd</c> box), without decoding anything.
/// </summary>
/// <remarks>
/// The box precedes the codestream in the files of every encoder: the answer is then known at the first bytes of the
/// first frame. A bare codestream has no box. Otherwise the box can only follow the codestream, and only a frame that
/// could be the one of a JPEG file is worth looking past: the rest of the container is then read, box by box.
/// </remarks>
internal sealed class JpegXlReconstructionProbe : ImageParser<bool>
{
    private readonly JpegXlStructureParser _structure;
    private readonly Observer _observer;

    public JpegXlReconstructionProbe(ImageCodecContext context)
        : base(ImageFormat.JpegXl)
    {
        _observer = new Observer();
        _structure = new JpegXlStructureParser(context, StructureWalk.Decode, _observer);
    }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
        => _structure.Parse(buffer, isEndOfInput, out consumed);

    public override bool GetResult() => _structure.HasJpegReconstructionData;

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

    private sealed class Observer : JpegXlDecodeObserver
    {
        public override bool WantsFrames => false;

        public override void OnHeaderComplete(JpegXlStructureParser structure)
        {
        }

        public override bool EndsAtHeader(JpegXlStructureParser structure) => structure.HasJpegReconstructionData || !structure.IsContainer;

        public override bool OnFrame(JpegXlStructureParser structure, JpegXlFrameHeader frame, JpegXlToc toc, WebPPayloadBuffer sections) => false;

        public override bool OnFrameSkipped(JpegXlStructureParser structure, JpegXlFrameHeader frame)
            => !structure.HasJpegReconstructionData && JpegXlJpegReconstructor.IsJpegFrame(structure.Header, frame);

        public override Image GetResult() => throw new InvalidOperationException("The probe of a JPEG XL file gives no image.");
    }
}
