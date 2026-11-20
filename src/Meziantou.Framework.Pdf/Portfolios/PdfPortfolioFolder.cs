using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A folder of a portfolio (<c>/Folders</c>, ISO 32000-2, 12.3.5, table 157): a node of the tree viewers show the files in.</summary>
public sealed class PdfPortfolioFolder
{
    private static readonly PdfName IDName = PdfName.Create("ID");
    private static readonly PdfName ChildName = PdfName.Create("Child");

    internal PdfPortfolioFolder(PdfDictionary dictionary, int depth)
    {
        Dictionary = dictionary;
        Depth = depth;
    }

    /// <summary>Gets the folder dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the name of the folder (<c>/Name</c>).</summary>
    public string Name => Dictionary.GetText(PdfNames.Name) ?? "";

    /// <summary>Gets the identifier of the folder (<c>/ID</c>): the files of the folder have names that start with it, as <c>&lt;ID&gt;name</c>.</summary>
    public int Id => (int)Math.Clamp(Dictionary.GetInteger(IDName, 0), 0, int.MaxValue);

    /// <summary>Gets the sub-folders (<c>/Child</c>, then the <c>/Next</c> of each one). The tree is bounded: a folder reached twice ends a list.</summary>
    public IReadOnlyList<PdfPortfolioFolder> Children
    {
        get
        {
            var result = new List<PdfPortfolioFolder>();
            if (Depth >= PdfPortfolio.MaxDepth)
                return result;

            var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            var current = Dictionary.Get<PdfDictionary>(ChildName);
            while (current is not null && result.Count < PdfPortfolio.MaxFolders && visited.Add(current))
            {
                result.Add(new PdfPortfolioFolder(current, Depth + 1));
                current = current.Get<PdfDictionary>(PdfNames.Next);
            }

            return result;
        }
    }

    internal int Depth { get; }
}
