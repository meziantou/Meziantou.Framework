using Meziantou.Framework.Pdf.Filters.Jbig2;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The <c>JBIG2Decode</c> filter (ISO 32000-2, 7.4.7): the embedded stream organization of ITU-T T.88, whose global
/// segments are in the <c>/JBIG2Globals</c> stream of the parameters. The output is one bit per pixel, 0 for black.
/// </summary>
internal static class Jbig2Filter
{
    public static byte[] Decode(ReadOnlySpan<byte> data, PdfDictionary? parameters, long limit)
    {
        var decoder = new Jbig2Decoder(limit);
        if (parameters?.GetResolved(PdfName.Create("JBIG2Globals")) is PdfStream globals)
        {
            decoder.DecodeSegments(globals.GetDecodedData());
        }

        decoder.DecodeSegments(data.ToArray());
        return decoder.GetPage();
    }
}
