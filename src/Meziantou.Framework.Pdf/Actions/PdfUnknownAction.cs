using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An action of a type that has no class of its own: a thread, a sound, a movie, a rendition, a transition, a 3D view, rich media, or a type the specification does not define. Read its <see cref="PdfAction.Dictionary"/>.</summary>
public sealed class PdfUnknownAction : PdfAction
{
    internal PdfUnknownAction(PdfDictionary dictionary, PdfDocument? document)
        : base(dictionary, document)
    {
    }
}
