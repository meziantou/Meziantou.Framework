using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A link annotation (ISO 32000-2, 12.5.6.5): a region of a page that opens a URI or goes to a destination.</summary>
public sealed class PdfLinkAnnotation : PdfAnnotation
{
    internal PdfLinkAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
        : base(page, dictionary, reference)
    {
    }

    /// <summary>Gets the URI the link opens (a <c>URI</c> action, 12.6.4.8), or <see langword="null"/> when the link has no URI or it is not a valid URI.</summary>
    public Uri? Uri
    {
        get
        {
            if (Dictionary.Get<PdfDictionary>(PdfNames.A) is { } action && action.GetName(PdfNames.S) == PdfNames.URI && action.Get<PdfString>(PdfNames.URI) is { } uri
                && Uri.TryCreate(System.Text.Encoding.UTF8.GetString(uri.Bytes.Span), UriKind.RelativeOrAbsolute, out var result))
            {
                return result;
            }

            return null;
        }
    }

    /// <summary>Gets the destination the link goes to (a <c>/Dest</c> entry or a <c>GoTo</c> action), or <see langword="null"/>.</summary>
    public PdfDestination? Destination
    {
        get
        {
            if (PdfDestination.Read(Page.Document, Dictionary[PdfNames.Dest]) is { } destination)
                return destination;

            if (Dictionary.Get<PdfDictionary>(PdfNames.A) is { } action && action.GetName(PdfNames.S) == PdfNames.GoTo)
                return PdfDestination.Read(Page.Document, action[PdfNames.D]);

            return null;
        }
    }
}
