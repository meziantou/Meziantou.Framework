using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A field of the schema of a portfolio (ISO 32000-2, 12.3.5, table 155): a column of the list of the files.</summary>
public sealed class PdfPortfolioField
{
    private static readonly PdfName OName = PdfName.Create("O");
    private static readonly PdfName VName = PdfName.Create("V");
    private static readonly PdfName EName = PdfName.Create("E");

    internal PdfPortfolioField(string key, PdfDictionary dictionary)
    {
        Key = key;
        Dictionary = dictionary;
    }

    /// <summary>Gets the key of the field: the name of its values in the files (<see cref="PdfEmbeddedFile.GetPortfolioValue(string)"/>).</summary>
    public string Key { get; }

    /// <summary>Gets the field dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets or sets the name viewers show for the field (<c>/N</c>).</summary>
    public string Name
    {
        get => Dictionary.GetText(PdfNames.N) ?? Key;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Dictionary.SetText(PdfNames.N, value);
        }
    }

    /// <summary>Gets the type of the values of the field (<c>/Subtype</c>).</summary>
    public PdfPortfolioFieldType Type => Dictionary.GetName(PdfNames.Subtype)?.Value switch
    {
        "D" => PdfPortfolioFieldType.Date,
        "N" => PdfPortfolioFieldType.Number,
        "F" => PdfPortfolioFieldType.FileName,
        "Desc" => PdfPortfolioFieldType.Description,
        "ModDate" => PdfPortfolioFieldType.ModificationDate,
        "CreationDate" => PdfPortfolioFieldType.CreationDate,
        "Size" => PdfPortfolioFieldType.Size,
        "CompressedSize" => PdfPortfolioFieldType.CompressedSize,
        _ => PdfPortfolioFieldType.Text,
    };

    /// <summary>Gets or sets the position of the field among the columns (<c>/O</c>), or <see langword="null"/>.</summary>
    public int? Order
    {
        get => Dictionary.GetResolved(OName) is PdfInteger { Value: >= int.MinValue and <= int.MaxValue } order ? (int)order.Value : null;
        set => Dictionary[OName] = value is { } order ? PdfInteger.Get(order) : null;
    }

    /// <summary>Gets or sets a value indicating whether viewers show the field (<c>/V</c>). The default is <see langword="true"/>.</summary>
    public bool IsVisible
    {
        get => Dictionary.GetBoolean(VName, defaultValue: true);
        set => Dictionary[VName] = value ? null : PdfBoolean.False;
    }

    /// <summary>Gets or sets a value indicating whether viewers let the user edit the values of the field (<c>/E</c>).</summary>
    public bool IsEditable
    {
        get => Dictionary.GetBoolean(EName);
        set => Dictionary[EName] = value ? PdfBoolean.True : null;
    }

    internal static PdfName GetTypeName(PdfPortfolioFieldType type) => PdfName.Create(type switch
    {
        PdfPortfolioFieldType.Text => "S",
        PdfPortfolioFieldType.Date => "D",
        PdfPortfolioFieldType.Number => "N",
        PdfPortfolioFieldType.FileName => "F",
        PdfPortfolioFieldType.Description => "Desc",
        PdfPortfolioFieldType.ModificationDate => "ModDate",
        PdfPortfolioFieldType.CreationDate => "CreationDate",
        PdfPortfolioFieldType.Size => "Size",
        PdfPortfolioFieldType.CompressedSize => "CompressedSize",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The field type is not valid"),
    });
}
