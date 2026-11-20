using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A portfolio (<c>/Collection</c>, ISO 32000-2, 12.3.5): a document whose embedded files viewers show as a list, with
/// the fields of a schema, sorted and in folders. The files are the embedded files of the document
/// (<see cref="PdfDocument.EmbeddedFiles"/>), and their values are set with <see cref="PdfEmbeddedFile.SetPortfolioValue(string, string?)"/>.
/// </summary>
public sealed class PdfPortfolio
{
    // The folders read: their depth and number are bounded
    internal const int MaxDepth = 32;
    internal const int MaxFolders = 4096;

    private static readonly PdfName SchemaName = PdfName.Create("Schema");
    private static readonly PdfName ViewName = PdfName.Create("View");
    private static readonly PdfName SortName = PdfName.Create("Sort");
    private static readonly PdfName FoldersName = PdfName.Create("Folders");
    private static readonly PdfName CollectionSchemaName = PdfName.Create("CollectionSchema");
    private static readonly PdfName CollectionFieldName = PdfName.Create("CollectionField");
    private static readonly PdfName CollectionSortName = PdfName.Create("CollectionSort");

    internal PdfPortfolio(PdfDictionary dictionary)
    {
        Dictionary = dictionary;
    }

    /// <summary>Gets the collection dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets or sets how viewers show the files (<c>/View</c>).</summary>
    public PdfPortfolioView View
    {
        get => Dictionary.GetName(ViewName)?.Value switch
        {
            "T" => PdfPortfolioView.Tile,
            "H" => PdfPortfolioView.Hidden,
            "C" => PdfPortfolioView.Custom,
            _ => PdfPortfolioView.Details,
        };
        set => Dictionary[ViewName] = value switch
        {
            PdfPortfolioView.Details => null,
            PdfPortfolioView.Tile => PdfName.Create("T"),
            PdfPortfolioView.Hidden => PdfName.Create("H"),
            PdfPortfolioView.Custom => PdfName.Create("C"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The view is not valid"),
        };
    }

    /// <summary>Gets or sets the name of the embedded file viewers show first (<c>/D</c>): its key in <see cref="PdfDocument.EmbeddedFiles"/>, or <see langword="null"/> for the document itself.</summary>
    public string? InitialFile
    {
        get => Dictionary.GetText(PdfNames.D);
        set => Dictionary.SetText(PdfNames.D, value);
    }

    /// <summary>Gets the fields of the schema (<c>/Schema</c>), in their order (<see cref="PdfPortfolioField.Order"/>, then the order of the dictionary).</summary>
    public IReadOnlyList<PdfPortfolioField> Fields
    {
        get
        {
            var result = new List<PdfPortfolioField>();
            if (Dictionary.Get<PdfDictionary>(SchemaName) is { } schema)
            {
                foreach (var (key, value) in schema)
                {
                    if (key != PdfNames.Type && (value is null ? null : PdfReference.ResolveIfReference(value)) is PdfDictionary fieldDictionary && result.Count < MaxFolders)
                    {
                        result.Add(new PdfPortfolioField(key.Value, fieldDictionary));
                    }
                }
            }

            return [.. result.OrderBy(item => item.Order ?? int.MaxValue)];
        }
    }

    /// <summary>Gets the keys of the fields the files are sorted by (<c>/Sort /S</c>), the first one first.</summary>
    public IReadOnlyList<string> SortFields => Dictionary.Get<PdfDictionary>(SortName)?.GetResolved(PdfNames.S) switch
    {
        PdfName name => [name.Value],
        PdfArray array => [.. array.Take(64).Select(item => (PdfReference.ResolveIfReference(item) as PdfName)?.Value).OfType<string>()],
        _ => [],
    };

    /// <summary>Gets a value indicating whether the files are sorted in ascending order of the first field (<c>/Sort /A</c>, the default).</summary>
    public bool SortAscending => Dictionary.Get<PdfDictionary>(SortName)?.GetResolved(PdfNames.A) switch
    {
        PdfBoolean ascending => ascending.Value,
        PdfArray { Count: > 0 } array when array.GetResolved(0) is PdfBoolean first => first.Value,
        _ => true,
    };

    /// <summary>Gets the root of the folders (<c>/Folders</c>), or <see langword="null"/> when the files are not in folders.</summary>
    public PdfPortfolioFolder? RootFolder => Dictionary.Get<PdfDictionary>(FoldersName) is { } root ? new PdfPortfolioFolder(root, depth: 0) : null;

    /// <summary>Adds a field to the schema, or replaces the field of the same key.</summary>
    /// <param name="key">The key of the field: the name of its values in the files.</param>
    /// <param name="name">The name viewers show for the field.</param>
    /// <param name="type">The type of the values.</param>
    /// <returns>The field. Its order is after the fields of the schema.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    public PdfPortfolioField AddField(string key, string name, PdfPortfolioFieldType type = PdfPortfolioFieldType.Text)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(name);
        if (Dictionary.Get<PdfDictionary>(SchemaName) is not { } schema)
        {
            schema = new PdfDictionary { [PdfNames.Type] = CollectionSchemaName };
            Dictionary[SchemaName] = schema;
        }

        var order = Fields.Count;
        var field = new PdfDictionary
        {
            [PdfNames.Type] = CollectionFieldName,
            [PdfNames.Subtype] = PdfPortfolioField.GetTypeName(type),
            [PdfNames.N] = PdfString.FromText(name),
            [PdfName.Create("O")] = PdfInteger.Get(order),
        };
        schema[PdfName.Create(key)] = field;
        return new PdfPortfolioField(key, field);
    }

    /// <summary>Sets the fields the files are sorted by (<c>/Sort</c>).</summary>
    /// <param name="ascending">Whether the files are sorted in ascending order.</param>
    /// <param name="keys">The keys of the fields, the first one first; none removes the sort.</param>
    public void SetSort(bool ascending, params IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var list = keys.ToList();
        if (list.Count == 0)
        {
            Dictionary.Remove(SortName);
            return;
        }

        Dictionary[SortName] = new PdfDictionary
        {
            [PdfNames.Type] = CollectionSortName,
            [PdfNames.S] = list.Count == 1 ? PdfName.Create(list[0]) : new PdfArray(list.Select(key => (PdfObject)PdfName.Create(key))),
            [PdfNames.A] = PdfBoolean.Get(ascending),
        };
    }
}
