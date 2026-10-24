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

    /// <summary>Adds a file attachment annotation (12.5.6.15) that embeds a new file.</summary>
    /// <param name="rectangle">The region of the icon, in default user space.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The attributes of the file, or <see langword="null"/> for the defaults.</param>
    /// <param name="icon">The icon of the annotation.</param>
    /// <returns>The annotation. Its <see cref="PdfAnnotation.Contents"/>, the text that describes it, is the description of the file or its name.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is empty, or the MIME type is not valid.</exception>
    public PdfFileAttachmentAnnotation AddFileAttachment(PdfRectangle rectangle, string fileName, ReadOnlySpan<byte> data, PdfEmbeddedFileOptions? options = null, PdfFileAttachmentIcon icon = PdfFileAttachmentIcon.PushPin)
    {
        var (dictionary, reference) = PdfEmbeddedFile.Create(_page.Document, fileName, data, options);
        return AddFileAttachment(rectangle, new PdfEmbeddedFile(_page.Document, dictionary, reference, key: null), icon);
    }

    /// <summary>Adds a file attachment annotation (12.5.6.15) that holds a file of the document, such as a file of <see cref="PdfDocument.EmbeddedFiles"/>.</summary>
    /// <param name="rectangle">The region of the icon, in default user space.</param>
    /// <param name="file">The file.</param>
    /// <param name="icon">The icon of the annotation.</param>
    /// <returns>The annotation. Its <see cref="PdfAnnotation.Contents"/>, the text that describes it, is the description of the file or its name.</returns>
    /// <exception cref="ArgumentException">The file is not an indirect object of the document.</exception>
    public PdfFileAttachmentAnnotation AddFileAttachment(PdfRectangle rectangle, PdfEmbeddedFile file, PdfFileAttachmentIcon icon = PdfFileAttachmentIcon.PushPin)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Document != _page.Document || file.Reference is not { } fileReference)
            throw new ArgumentException("The file is not an indirect object of the document of the page", nameof(file));

        var iconName = PdfFileAttachmentAnnotation.GetIconName(icon);
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfFileAttachmentAnnotation.FileAttachmentName,
            [PdfNames.Rect] = rectangle.ToArray(),
            [PdfNames.Contents] = PdfString.FromText(file.Description ?? file.FileName),
            [PdfNames.P] = _page.Reference,

            // Print, NoZoom and NoRotate (12.5.3): the icon keeps its size and orientation
            [PdfNames.F] = PdfInteger.Get(4 | 8 | 16),
            [PdfFileAttachmentAnnotation.FSName] = fileReference,
            [PdfNames.Name] = iconName,

            // The file is associated with the annotation (14.13.2), which PDF/A-3 requires of every embedded file
            [PdfEmbeddedFile.AFName] = new PdfArray(fileReference),
            [PdfName.Create("AP")] = new PdfDictionary { [PdfNames.N] = CreateIcon(rectangle) },
        };
        var reference = _page.Document.Objects.Add(dictionary);
        GetOrCreateArray().Add(reference);
        return new PdfFileAttachmentAnnotation(_page, dictionary, reference);
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

    /// <summary>
    /// Creates the appearance of a file attachment (12.5.5): a sheet with a folded corner, drawn in the rectangle, which
    /// PDF/A requires (ISO 19005-2, 6.3.3) and viewers show instead of their own icon.
    /// </summary>
    private PdfReference CreateIcon(PdfRectangle rectangle)
    {
        var (width, height) = (Math.Max(rectangle.Width, 1), Math.Max(rectangle.Height, 1));
        var form = _page.Document.CreateForm(PdfRectangle.FromSize(0, 0, width, height));
        var fold = Math.Min(width, height) * 0.3;
        var line = Math.Max(Math.Min(width, height) * 0.06, 0.5);
        form.CreateContentBuilder()
            .SetFillGray(1)
            .SetStrokeGray(0.25)
            .SetLineWidth(line)
            .SetLineJoin(PdfLineJoin.Round)
            .MoveTo(line, line)
            .LineTo(width - line, line)
            .LineTo(width - line, height - fold)
            .LineTo(width - fold, height - line)
            .LineTo(line, height - line)
            .ClosePath()
            .FillAndStroke()
            .MoveTo(width - fold, height - line)
            .LineTo(width - fold, height - fold)
            .LineTo(width - line, height - fold)
            .Stroke();
        return form.Reference;
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
