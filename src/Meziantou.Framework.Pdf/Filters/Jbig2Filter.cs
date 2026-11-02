using System.Runtime.CompilerServices;
using Meziantou.Framework.Pdf.Filters.Jbig2;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The <c>JBIG2Decode</c> filter (ISO 32000-2, 7.4.7): the embedded stream organization of ITU-T T.88, whose global
/// segments are in the <c>/JBIG2Globals</c> stream of the parameters. The output is one bit per pixel, 0 for black.
/// </summary>
/// <remarks>
/// The images of a document often share their global segments (a symbol dictionary for all the pages of a scan): they are
/// decoded once per <c>/JBIG2Globals</c> stream and limit, and each image starts from their results. A change of the
/// data, the filters or the parameters of the stream decodes them again.
/// </remarks>
internal static class Jbig2Filter
{
    private static readonly PdfName Jbig2GlobalsName = PdfName.Create("JBIG2Globals");

    private static readonly ConditionalWeakTable<PdfStream, CachedGlobals> GlobalsCache = [];

    public static byte[] Decode(ReadOnlySpan<byte> data, PdfDictionary? parameters, long limit)
    {
        Jbig2Decoder decoder;
        if (parameters?.GetResolved(Jbig2GlobalsName) is PdfStream globals)
        {
            // The global segments are not an image: a JBIG2Decode filter there would decode its own globals again
            var filters = globals.Filters;
            if (filters.Any(filter => filter.Value == "JBIG2Decode"))
                throw new InvalidPdfContentException("The /JBIG2Globals stream uses the JBIG2Decode filter");

            var key = CreateKey(globals, limit);
            if (GlobalsCache.TryGetValue(globals, out var cached) && cached.Matches(key))
            {
                decoder = new Jbig2Decoder(limit, cached.Globals!);
            }
            else
            {
                decoder = new Jbig2Decoder(limit);
                decoder.DecodeSegments(globals.GetDecodedData());
                if (decoder.GetGlobals() is { } decoded)
                {
                    GlobalsCache.AddOrUpdate(globals, key with { Globals = decoded });
                }
            }
        }
        else
        {
            decoder = new Jbig2Decoder(limit);
        }

        decoder.DecodeSegments(data.ToArray());
        return decoder.GetPage();
    }

    /// <summary>Gets a value indicating whether the global segments of a stream are decoded for a limit, and still match the stream.</summary>
    internal static bool IsCached(PdfStream globals, long limit) => GlobalsCache.TryGetValue(globals, out var cached) && cached.Matches(CreateKey(globals, limit));

    private static CachedGlobals CreateKey(PdfStream globals, long limit) => new(globals.EncodedData, globals.DataVersion, globals.Filters, globals.Dictionary.GetResolved(PdfNames.DecodeParms), limit, Globals: null);

    /// <summary>The decoded global segments of a stream, and what they were decoded from.</summary>
    private sealed record CachedGlobals(ReadOnlyMemory<byte> EncodedData, int DataVersion, IReadOnlyList<PdfName> Filters, PdfObject? DecodeParameters, long Limit, Jbig2Globals? Globals)
    {
        public bool Matches(CachedGlobals other)
        {
            return EncodedData.Equals(other.EncodedData)
                && DataVersion == other.DataVersion
                && Filters.SequenceEqual(other.Filters)
                && ReferenceEquals(DecodeParameters, other.DecodeParameters)
                && Limit == other.Limit;
        }
    }
}
