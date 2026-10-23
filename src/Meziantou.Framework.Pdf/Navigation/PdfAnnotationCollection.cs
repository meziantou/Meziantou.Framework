using System.Collections;
using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The annotations of a page: the <c>/Annots</c> array (ISO 32000-2, 12.5).</summary>
public sealed class PdfAnnotationCollection : IReadOnlyList<PdfAnnotation>
{
    private readonly PdfPage _page;

    internal PdfAnnotationCollection(PdfPage page)
    {
        _page = page;
    }

    /// <summary>Gets the number of annotations.</summary>
    public int Count => GetItems().Count;

    /// <summary>Gets an annotation.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The annotation.</returns>
    public PdfAnnotation this[int index] => GetItems()[index];

    /// <summary>Adds a link that opens a URI.</summary>
    /// <param name="rectangle">The region of the page, in default user space.</param>
    /// <param name="uri">The URI: an absolute URI, written as 7-bit ASCII (12.6.4.8).</param>
    /// <returns>The link.</returns>
    public PdfLinkAnnotation AddLink(PdfRectangle rectangle, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("The URI must be absolute", nameof(uri));

        var action = new PdfDictionary
        {
            [PdfNames.S] = PdfNames.URI,
            [PdfNames.URI] = PdfString.FromBytes(Encoding.ASCII.GetBytes(uri.AbsoluteUri)),
        };
        return AddLink(rectangle, PdfNames.A, action);
    }

    /// <summary>Adds a link that goes to a destination of the document.</summary>
    /// <param name="rectangle">The region of the page, in default user space.</param>
    /// <param name="destination">The destination.</param>
    /// <returns>The link.</returns>
    public PdfLinkAnnotation AddLink(PdfRectangle rectangle, PdfDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Page.Document != _page.Document)
            throw new ArgumentException("The destination is a page of another document", nameof(destination));

        return AddLink(rectangle, PdfNames.Dest, destination.ToArray());
    }

    /// <summary>Removes an annotation from the page.</summary>
    /// <param name="annotation">The annotation.</param>
    /// <returns><see langword="true"/> when the annotation was on the page.</returns>
    public bool Remove(PdfAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (_page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } array)
            return false;

        for (var i = 0; i < array.Count; i++)
        {
            if ((annotation.Reference is not null && annotation.Reference.Equals(array[i])) || ReferenceEquals(array[i], annotation.Dictionary))
            {
                array.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns an enumerator over the annotations.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<PdfAnnotation> GetEnumerator() => GetItems().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private PdfLinkAnnotation AddLink(PdfRectangle rectangle, PdfName key, PdfObject target)
    {
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfNames.Link,
            [PdfNames.Rect] = rectangle.ToArray(),
            [PdfNames.Border] = PdfArray.FromNumbers(0, 0, 0),
            [PdfNames.P] = _page.Reference,
            [PdfNames.F] = PdfInteger.Get(4), // Print (12.5.3)
            [key] = target,
        };
        var reference = _page.Document.Objects.Add(dictionary);
        GetOrCreateArray().Add(reference);
        return new PdfLinkAnnotation(_page, dictionary, reference);
    }

    private PdfArray GetOrCreateArray()
    {
        if (_page.Dictionary.Get<PdfArray>(PdfNames.Annots) is { } array)
            return array;

        array = new PdfArray();
        _page.Dictionary[PdfNames.Annots] = array;
        return array;
    }

    private List<PdfAnnotation> GetItems()
    {
        var result = new List<PdfAnnotation>();
        if (_page.Dictionary.Get<PdfArray>(PdfNames.Annots) is { } array)
        {
            foreach (var item in array)
            {
                if (PdfReference.ResolveIfReference(item) is PdfDictionary dictionary)
                {
                    result.Add(PdfAnnotation.Create(_page, dictionary, item as PdfReference));
                }
            }
        }

        return result;
    }
}
