using System.Buffers;
using Meziantou.Framework.Pdf.Filters;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>A stream: a dictionary and a sequence of bytes (ISO 32000-2, 7.3.8). It is mutable and not thread-safe.</summary>
/// <remarks>
/// <para>
/// The data is kept encoded, as the <c>/Filter</c> and <c>/DecodeParms</c> entries of the dictionary describe it.
/// <see cref="GetDecodedData"/> applies the filters. <see cref="SetData(ReadOnlySpan{byte})"/> stores data without filter: the writer
/// compresses it when <see cref="PdfSaveOptions.CompressStreams"/> is set. The writer always writes the
/// <c>/Length</c> entry from the data.
/// </para>
/// <para>A stream is always an indirect object: add it to <see cref="PdfIndirectObjectCollection"/> and refer to it with the reference.</para>
/// </remarks>
public sealed class PdfStream : PdfObject
{
    private ReadOnlyMemory<byte> _encoded;
    private ArrayBufferWriter<byte>? _appended;

    /// <summary>Initializes a new instance of the <see cref="PdfStream"/> class with an empty dictionary and no data.</summary>
    public PdfStream()
        : this(new PdfDictionary())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfStream"/> class.</summary>
    /// <param name="dictionary">The dictionary of the stream.</param>
    public PdfStream(PdfDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        Dictionary = dictionary;
    }

    /// <summary>Initializes a new instance of the <see cref="PdfStream"/> class with data without filter.</summary>
    /// <param name="dictionary">The dictionary of the stream.</param>
    /// <param name="data">The data.</param>
    public PdfStream(PdfDictionary dictionary, ReadOnlySpan<byte> data)
        : this(dictionary)
    {
        SetData(data);
    }

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Stream;

    /// <summary>Gets the dictionary of the stream.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the data of the stream, encoded with its filters.</summary>
    public ReadOnlyMemory<byte> EncodedData
    {
        get
        {
            Flush();
            return _encoded;
        }
    }

    /// <summary>Gets the names of the filters of the stream, in the order they are applied to decode it.</summary>
    public IReadOnlyList<PdfName> Filters => PdfFilterPipeline.GetFilterNames(Dictionary);

    /// <summary>Gets a value incremented each time the data changes.</summary>
    internal int DataVersion { get; private set; }

    /// <summary>Gets a value indicating whether the writer may compress the data: it was set without filter by the library or its caller.</summary>
    internal bool IsCompressible { get; private set; }

    /// <summary>Gets or sets the limits the decoding applies (the read options of the document the stream was loaded from).</summary>
    internal PdfDecodeLimits DecodeLimits { get; set; } = PdfDecodeLimits.Default;

    /// <summary>Decodes the data with the filters of the stream.</summary>
    /// <returns>The decoded data. For an image filter (<c>DCTDecode</c>, <c>JPXDecode</c>), the samples of the image.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not valid for a filter, or a filter is unknown.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">A filter is not supported.</exception>
    /// <exception cref="PdfResourceLimitException">The decoded data exceeds <see cref="PdfReadOptions.MaxDecodedStreamLength"/>.</exception>
    public byte[] GetDecodedData() => PdfFilterPipeline.Decode(this, stopBeforeImageFilters: false, out _);

    /// <summary>Replaces the data with data without filter, and removes the <c>/Filter</c> and <c>/DecodeParms</c> entries.</summary>
    /// <param name="data">The data.</param>
    public void SetData(ReadOnlySpan<byte> data)
    {
        Dictionary.Remove(PdfNames.Filter);
        Dictionary.Remove(PdfNames.DecodeParms);
        Dictionary.Remove(PdfNames.DL);
        _appended = null;
        _encoded = data.ToArray();
        IsCompressible = true;
        DataVersion++;
        Dictionary[PdfNames.Length] = PdfInteger.Get(_encoded.Length);
    }

    /// <summary>Replaces the data with data already encoded with the filters the dictionary declares.</summary>
    /// <param name="data">The encoded data.</param>
    public void SetEncodedData(ReadOnlySpan<byte> data) => SetData(data.ToArray(), compressible: false);

    /// <summary>Replaces the data with data encoded with one filter, and sets the <c>/Filter</c> and <c>/DecodeParms</c> entries.</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="filter">The filter that decodes the data, such as <c>FlateDecode</c> or <c>DCTDecode</c>.</param>
    /// <param name="decodeParameters">The parameters of the filter, or <see langword="null"/>.</param>
    public void SetEncodedData(ReadOnlySpan<byte> data, PdfName filter, PdfDictionary? decodeParameters = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        Dictionary[PdfNames.Filter] = filter;
        Dictionary[PdfNames.DecodeParms] = decodeParameters;
        SetData(data.ToArray(), compressible: false);
    }

    internal void SetEncodedData(ReadOnlyMemory<byte> data) => SetData(data, compressible: false);

    /// <summary>Replaces the data without copying it.</summary>
    /// <param name="data">The data, encoded with the filters of the dictionary.</param>
    /// <param name="compressible">Whether the writer may compress the data (it has no filter).</param>
    internal void SetData(ReadOnlyMemory<byte> data, bool compressible)
    {
        _appended = null;
        _encoded = data;
        IsCompressible = compressible;
        DataVersion++;
        Dictionary[PdfNames.Length] = PdfInteger.Get(data.Length);
    }

    /// <summary>Sets the data the parser read: the dictionary is not changed.</summary>
    internal void SetLoadedData(ReadOnlyMemory<byte> data)
    {
        _encoded = data;
        IsCompressible = false;
    }

    /// <summary>Appends data to a stream without filter (content builders).</summary>
    internal void AppendData(ReadOnlySpan<byte> data)
    {
        if (_appended is null)
        {
            if (Dictionary.ContainsKey(PdfNames.Filter))
                throw new InvalidOperationException("Data cannot be appended to an encoded stream");

            _appended = new ArrayBufferWriter<byte>(Math.Max(256, _encoded.Length + data.Length));
            _appended.Write(_encoded.Span);
            IsCompressible = true;
        }

        _appended.Write(data);
        DataVersion++;
    }

    private void Flush()
    {
        if (_appended is not null && _encoded.Length != _appended.WrittenCount)
        {
            _encoded = _appended.WrittenMemory;
            Dictionary[PdfNames.Length] = PdfInteger.Get(_encoded.Length);
        }
    }
}
