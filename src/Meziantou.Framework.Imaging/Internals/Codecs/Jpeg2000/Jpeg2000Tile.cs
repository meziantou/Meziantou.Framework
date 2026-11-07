namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What the codestream holds of one tile, collected from its tile-parts while the codestream is decoded: the parameters of
/// its tile-part headers, its packets, and its packet headers when they are packed in PPM or PPT marker segments.
/// </summary>
internal sealed class Jpeg2000Tile : IDisposable
{
    private JpegXlCodestreamBuffer? _packedHeaders;

    public Jpeg2000Tile(int index, AllocationScope scope, int componentCount)
    {
        Index = index;
        Scope = scope;
        Headers = new Jpeg2000HeaderSet(componentCount);
        Body = new JpegXlCodestreamBuffer(scope);
    }

    /// <summary>Gets the index of the tile, in raster order.</summary>
    public int Index { get; }

    public AllocationScope Scope { get; }

    /// <summary>Gets the parameters of the tile-part headers.</summary>
    public Jpeg2000HeaderSet Headers { get; }

    /// <summary>Gets the bytes that follow the SOD marker of every tile-part, in order.</summary>
    public JpegXlCodestreamBuffer Body { get; }

    /// <summary>Gets the packet headers of the tile when they are not in <see cref="Body"/>, or <see langword="null"/>.</summary>
    public JpegXlCodestreamBuffer? PackedHeaders => _packedHeaders;

    /// <summary>Gets or sets a value indicating whether the packet headers come from PPT marker segments.</summary>
    public bool HasTilePackedHeaders { get; set; }

    /// <summary>Appends packet headers of a PPM or PPT marker segment.</summary>
    public void AppendPackedHeaders(ReadOnlySpan<byte> data)
    {
        _packedHeaders ??= new JpegXlCodestreamBuffer(Scope);
        _packedHeaders.Append(data);
    }

    public void Dispose()
    {
        Body.Dispose();
        _packedHeaders?.Dispose();
        _packedHeaders = null;
    }
}
