namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel-decoding side of JPEG loading (baseline and progressive). <see cref="JpegStructureParser"/> walks the
/// markers in <see cref="StructureWalk.Decode"/> mode (bounded segments, frame header, scan headers, entropy-coded segment
/// boundaries, metadata, unsupported modes, limits) and calls these hooks; the observer parses the tables, decodes the
/// entropy-coded data and builds the image with a <see cref="DecodedImageBuilder"/>.
/// </summary>
/// <remarks>
/// Call order: <see cref="OnSegment"/> for every marker segment with a length (tables, DRI, APPn, COM, SOF...), in file
/// order; <see cref="OnHeaderComplete"/> once, when the first SOS marker is reached (before its <see cref="OnSegment"/>);
/// for each scan, <see cref="OnSegment"/> (SOS) then <see cref="OnEntropyData"/> with the raw entropy-coded bytes (byte
/// stuffing and RSTn markers included, unmodified) and <see cref="OnScanEnd"/>; finally <see cref="OnEnd"/> at EOI.
/// Payload spans exclude the marker and length bytes and are only valid during the call.
/// </remarks>
internal abstract class JpegDecodeObserver : IImageDecodeObserver
{
    /// <param name="marker">The marker code (the byte after 0xFF).</param>
    /// <param name="payload">The segment payload (after the 2-byte length field).</param>
    public virtual void OnSegment(byte marker, ReadOnlySpan<byte> payload)
    {
    }

    public abstract void OnHeaderComplete(JpegStructureParser structure);

    public virtual void OnEntropyData(ReadOnlySpan<byte> data)
    {
    }

    /// <returns><see langword="true"/> to continue the walk; <see langword="false"/> to stop.</returns>
    public virtual bool OnScanEnd() => true;

    public virtual void OnEnd(JpegStructureParser structure)
    {
    }

    public abstract Image GetResult();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
