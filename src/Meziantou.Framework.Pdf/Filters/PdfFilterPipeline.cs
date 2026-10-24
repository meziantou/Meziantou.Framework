using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>Applies the filters of a stream (ISO 32000-2, 7.4) in order.</summary>
internal static class PdfFilterPipeline
{
    public static IReadOnlyList<PdfName> GetFilterNames(PdfDictionary dictionary)
    {
        return dictionary.GetResolved(PdfNames.Filter) switch
        {
            PdfName name => [Normalize(name)],
            PdfArray array => [.. array.Select(PdfReference.ResolveIfReference).OfType<PdfName>().Select(Normalize)],
            _ => [],
        };
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
        var dictionary = stream.Dictionary;
        var filters = GetFilterNames(dictionary);
        var parameters = GetParameters(dictionary, filters.Count);
        return Decode(stream.EncodedData.Span, filters, parameters, stream.DecodeLimits, stopBeforeImageFilters, out imageFilter);
    }

    public static byte[] Decode(ReadOnlySpan<byte> data, IReadOnlyList<PdfName> filters, IReadOnlyList<PdfDictionary?> parameters, PdfDecodeLimits limits, bool stopBeforeImageFilters, out (PdfName Name, PdfDictionary? Parameters)? imageFilter)
    {
        imageFilter = null;
        var limit = limits.MaxDecodedStreamLength;
        byte[]? current = null;
        for (var i = 0; i < filters.Count; i++)
        {
            var input = current is null ? data : current;
            var filter = filters[i];
            var filterParameters = i < parameters.Count ? parameters[i] : null;
            if (stopBeforeImageFilters && IsImageFilter(filter))
            {
                imageFilter = (filter, filterParameters);
                return current ?? data.ToArray();
            }

            current = filter.Value switch
            {
                "FlateDecode" => Predictor.Decode(FlateFilter.Decode(input, limit), filterParameters, limit),
                "LZWDecode" => Predictor.Decode(LzwFilter.Decode(input, (int)(filterParameters?.GetInteger(PdfNames.EarlyChange, 1) ?? 1), limit), filterParameters, limit),
                "ASCIIHexDecode" => AsciiHexFilter.Decode(input, limit),
                "ASCII85Decode" => Ascii85Filter.Decode(input, limit),
                "RunLengthDecode" => RunLengthFilter.Decode(input, limit),
                "Crypt" => DecodeCrypt(input),
                "DCTDecode" => ImageFilters.DecodeDct(input, limit),
                "JPXDecode" => ImageFilters.DecodeJpx(input, limit),
                "CCITTFaxDecode" => CcittFaxFilter.Decode(input, filterParameters, limit),
                "JBIG2Decode" => Jbig2Filter.Decode(input, filterParameters, limit),
                _ => throw new InvalidPdfContentException($"Unknown filter '{filter.Value}'"),
            };
        }

        return current ?? data.ToArray();
    }

    public static IReadOnlyList<PdfDictionary?> GetParameters(PdfDictionary dictionary, int filterCount)
    {
        var value = dictionary.GetResolved(PdfNames.DecodeParms) ?? dictionary.GetResolved(PdfNames.DP);
        switch (value)
        {
            case PdfDictionary single:
                return [single];
            case PdfArray array:
                var result = new PdfDictionary?[Math.Max(filterCount, array.Count)];
                for (var i = 0; i < array.Count; i++)
                {
                    result[i] = array.GetResolved(i) as PdfDictionary;
                }

                return result;
            default:
                return [];
        }
    }

    private static byte[] DecodeCrypt(ReadOnlySpan<byte> input)
    {
        // The crypt filters are applied when the objects of an encrypted document are loaded (7.6.6): the data is
        // already decrypted, and the Identity crypt filter leaves it as it is
        return input.ToArray();
    }
}
