using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>An item of the document outline, a bookmark (ISO 32000-2, 12.3.3).</summary>
public sealed class PdfOutlineItem
{
    private string _title;
    private PdfDestination? _destination;
    private bool _isOpen;
    private PdfColor? _color;
    private bool _isBold;
    private bool _isItalic;

    /// <summary>Initializes a new instance of the <see cref="PdfOutlineItem"/> class.</summary>
    /// <param name="title">The text of the item.</param>
    /// <param name="destination">The destination, or <see langword="null"/>.</param>
    public PdfOutlineItem(string title, PdfDestination? destination = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        _title = title;
        _destination = destination;
        Children = new PdfOutlineCollection();
        Changes = OutlineItemChanges.All;
    }

    internal PdfOutlineItem(string title, PdfDestination? destination, bool isOpen, PdfColor? color, bool isBold, bool isItalic, PdfDictionary dictionary, PdfReference reference)
    {
        _title = title;
        _destination = destination;
        _isOpen = isOpen;
        _color = color;
        _isBold = isBold;
        _isItalic = isItalic;
        Dictionary = dictionary;
        Reference = reference;
        Children = new PdfOutlineCollection();
    }

    /// <summary>Gets or sets the text of the item.</summary>
    public string Title
    {
        get => _title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _title = value;
            Changes |= OutlineItemChanges.Title;
        }
    }

    /// <summary>Gets or sets the destination, or <see langword="null"/> when the item has none or has an action of another kind.</summary>
    public PdfDestination? Destination
    {
        get => _destination;
        set
        {
            _destination = value;
            Changes |= OutlineItemChanges.Destination;
        }
    }

    /// <summary>Gets or sets a value indicating whether the children of the item are shown when the document is opened.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            _isOpen = value;
            Changes |= OutlineItemChanges.Count;
        }
    }

    /// <summary>Gets or sets the color of the text (PDF 1.4), or <see langword="null"/> for black. It must be an RGB color.</summary>
    /// <exception cref="ArgumentException">The color is not an RGB color.</exception>
    public PdfColor? Color
    {
        get => _color;
        set
        {
            if (value is { ColorSpace: not PdfDeviceColorSpace.Rgb })
                throw new ArgumentException("The color of an outline item must be an RGB color", nameof(value));

            _color = value;
            Changes |= OutlineItemChanges.Style;
        }
    }

    /// <summary>Gets or sets a value indicating whether the text is bold (PDF 1.4).</summary>
    public bool IsBold
    {
        get => _isBold;
        set
        {
            _isBold = value;
            Changes |= OutlineItemChanges.Style;
        }
    }

    /// <summary>Gets or sets a value indicating whether the text is italic (PDF 1.4).</summary>
    public bool IsItalic
    {
        get => _isItalic;
        set
        {
            _isItalic = value;
            Changes |= OutlineItemChanges.Style;
        }
    }

    /// <summary>Gets the children of the item.</summary>
    public PdfOutlineCollection Children { get; }

    /// <summary>Gets the dictionary of an item read from a file: its other entries (an action) are kept.</summary>
    internal PdfDictionary? Dictionary { get; set; }

    internal PdfReference? Reference { get; set; }

    internal OutlineItemChanges Changes { get; set; }
}
