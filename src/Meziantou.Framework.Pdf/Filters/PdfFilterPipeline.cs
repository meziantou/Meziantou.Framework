using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>Applies the filters of a stream (ISO 32000-2, 7.4) in order.</summary>
internal static class PdfFilterPipeline
{
    /// <summary>Gets the names of the filters of a dictionary; the entries of a <c>/Filter</c> array that are not names are left out.</summary>
    public static IReadOnlyList<PdfName> GetFilterNames(PdfDictionary dictionary)
    {
        return dictionary.GetResolved(PdfNames.Filter) switch
        {
            PdfName name => [Normalize(name)],
            PdfArray array => [.. array.Select(PdfReference.ResolveIfReference).OfType<PdfName>().Select(Normalize)],
            _ => [],
        };
    }

    /// <summary>Gets the filters of a dictionary and their parameters, paired by their position in the arrays.</summary>
    /// <exception cref="InvalidPdfContentException">An entry of the <c>/Filter</c> array is not a name.</exception>
    public static IReadOnlyList<(PdfName Name, PdfDictionary? Parameters)> GetFilters(PdfDictionary dictionary)
    {
        var parameters = dictionary.GetResolved(PdfNames.DecodeParms) ?? dictionary.GetResolved(PdfNames.DP);
        PdfDictionary? GetParameters(int index) => parameters switch
        {
            // A single dictionary goes with the first filter
            PdfDictionary single => index == 0 ? single : null,
            PdfArray array => index < array.Count ? array.GetResolved(index) as PdfDictionary : null,
            _ => null,
        };

        switch (dictionary.GetResolved(PdfNames.Filter))
        {
            case PdfName name:
                return [(Normalize(name), GetParameters(0))];

            case PdfArray array:
                var result = new (PdfName Name, PdfDictionary? Parameters)[array.Count];
                for (var i = 0; i < array.Count; i++)
                {
                    // An entry that is not a name cannot be skipped: the filters after it would get the parameters of another one
                    if (array.GetResolved(i) is not PdfName filter)
                        throw new InvalidPdfContentException($"The entry {i} of the /Filter array of a stream is not a name");

                    result[i] = (Normalize(filter), GetParameters(i));
                }

                return result;

            default:
                return [];
        }
    }

    /// <summary>Gets the full name of a filter from its abbreviation (inline images, 8.9.7).</summary>
    public static PdfName Normalize(PdfName name)
    {
        return name.Value switch
        {
            "AHx" => PdfNames.ASCIIHexDecode,
            "A85" => PdfNames.ASCII85Decode,
            "LZW" => PdfNames.LZWDecode,
            "Fl" => PdfNames.FlateDecode,
            "RL" => PdfNames.RunLengthDecode,
            "CCF" => PdfNames.CCITTFaxDecode,
            "DCT" => PdfNames.DCTDecode,
            _ => name,
        };
    }

    public static bool IsImageFilter(PdfName name) => name == PdfNames.DCTDecode || name == PdfNames.JPXDecode || name == PdfNames.CCITTFaxDecode || name == PdfNames.JBIG2Decode;

    /// <summary>Decodes the data of a stream.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="stopBeforeImageFilters">Whether to stop before the first image filter and return its input.</param>
    /// <param name="imageFilter">The image filter that was not applied, and its parameters.</param>
    public static byte[] Decode(PdfStream stream, bool stopBeforeImageFilters, out (PdfName Name, PdfDictionary? Parameters)? imageFilter)
    {
        return Decode(stream.EncodedData, stream.Dictionary, stream.DecodeLimits, stopBeforeImageFilters, out imageFilter);
    }

    /// <summary>Decodes data with the filters of a dictionary (a stream or an inline image).</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="dictionary">The dictionary that declares the filters and their parameters.</param>
    /// <param name="limits">The limits of the decoding.</param>
    /// <param name="stopBeforeImageFilters">
    /// Whether to stop before the first image filter and return its input. The input of <c>DCTDecode</c> gets an Adobe
    /// segment with the <c>/ColorTransform</c> of its parameters, so that the JPEG decoder transforms the colors as the
    /// filter would.
    /// </param>
    /// <param name="imageFilter">The image filter that was not applied, and its parameters.</param>
    /// <exception cref="InvalidPdfContentException">The data is not valid for a filter, or the filters are not declared correctly.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">A filter is unknown or not supported.</exception>
    /// <exception cref="PdfResourceLimitException">The decoded data exceeds the limits.</exception>
    public static byte[] Decode(ReadOnlyMemory<byte> data, PdfDictionary dictionary, PdfDecodeLimits limits, bool stopBeforeImageFilters, out (PdfName Name, PdfDictionary? Parameters)? imageFilter)
    {
        imageFilter = null;
        var filters = GetFilters(dictionary);
        var limit = limits.MaxDecodedStreamLength;
        byte[]? current = null;
        for (var i = 0; i < filters.Count; i++)
        {
            ReadOnlyMemory<byte> input = current is null ? data : current;
            var (filter, filterParameters) = filters[i];
            if (stopBeforeImageFilters && IsImageFilter(filter))
            {
                imageFilter = (filter, filterParameters);
                if (filter == PdfNames.DCTDecode && ImageFilters.ApplyColorTransform(input.Span, filterParameters) is { } transformed)
                    return transformed;

                return current ?? data.ToArray();
            }

            current = filter.Value switch
            {
                "FlateDecode" => Predictor.Decode(FlateFilter.Decode(input, limit), filterParameters, limit),
                "LZWDecode" => Predictor.Decode(LzwFilter.Decode(input.Span, (int)(filterParameters?.GetInteger(PdfNames.EarlyChange, 1) ?? 1), limit), filterParameters, limit),
                "ASCIIHexDecode" => AsciiHexFilter.Decode(input.Span, limit),
                "ASCII85Decode" => Ascii85Filter.Decode(input.Span, limit),
                "RunLengthDecode" => RunLengthFilter.Decode(input.Span, limit),
                "Crypt" => current ?? data.ToArray(), // The crypt filters are applied when the objects of an encrypted document are loaded (7.6.6)
                "DCTDecode" => ImageFilters.DecodeDct(input.Span, filterParameters, limit),
                "JPXDecode" => ImageFilters.DecodeJpx(input.Span, limit),
                "CCITTFaxDecode" => CcittFaxFilter.Decode(input.Span, filterParameters, limit),
                "JBIG2Decode" => Jbig2Filter.Decode(input.Span, filterParameters, limit),
                _ => throw new UnsupportedPdfFeatureException($"The filter '{filter.Value}' is not supported"),
            };
        }

        return current ?? data.ToArray();
    }
}
