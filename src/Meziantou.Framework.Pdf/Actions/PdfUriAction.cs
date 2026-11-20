using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action that opens a URI (<c>URI</c>, ISO 32000-2, 12.6.4.8).</summary>
public sealed class PdfUriAction : PdfAction
{
    internal PdfUriAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }

    /// <summary>Gets the URI, or <see langword="null"/> when it is missing or not a valid URI.</summary>
    public Uri? Uri => Dictionary.Get<PdfString>(PdfNames.URI) is { } uri && Uri.TryCreate(Encoding.UTF8.GetString(uri.Bytes.Span), UriKind.RelativeOrAbsolute, out var result) ? result : null;

    /// <summary>Gets a value indicating whether the coordinates of the mouse are appended to the URI (<c>/IsMap</c>).</summary>
    public bool IsMap => Dictionary.GetBoolean(PdfName.Create("IsMap"));
}
