using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A folder of a portfolio (<c>/Folders</c>, ISO 32000-2, 12.3.5, table 157): a node of the tree viewers show the files in.</summary>
public sealed class PdfPortfolioFolder
{
    private static readonly PdfName IDName = PdfName.Create("ID");
    private static readonly PdfName ChildName = PdfName.Create("Child");

    private readonly FolderTree _tree;
    private IReadOnlyList<PdfPortfolioFolder>? _children;

    private PdfPortfolioFolder(PdfDictionary dictionary, int depth, FolderTree tree)
    {
        Dictionary = dictionary;
        Depth = depth;
        _tree = tree;
    }

    /// <summary>Gets the folder dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the name of the folder (<c>/Name</c>).</summary>
    public string Name => Dictionary.GetText(PdfNames.Name) ?? "";

    /// <summary>Gets the identifier of the folder (<c>/ID</c>): the files of the folder have names that start with it, as <c>&lt;ID&gt;name</c>.</summary>
    public int Id => (int)Math.Clamp(Dictionary.GetInteger(IDName, 0), 0, int.MaxValue);

    /// <summary>
    /// Gets the sub-folders (<c>/Child</c>, then the <c>/Next</c> of each one). The tree is bounded: a folder is in the
    /// tree once, wherever the file refers to it again, and the tree has a bounded number of folders and depth.
    /// </summary>
    public IReadOnlyList<PdfPortfolioFolder> Children => _children ??= ReadChildren();

    internal int Depth { get; }

    internal static PdfPortfolioFolder CreateRoot(PdfDictionary root)
    {
        var tree = new FolderTree();
        tree.Visited.Add(root);
        return new PdfPortfolioFolder(root, depth: 0, tree);
    }

    private List<PdfPortfolioFolder> ReadChildren()
    {
        var result = new List<PdfPortfolioFolder>();
        if (Depth >= PdfPortfolio.MaxDepth)
            return result;

        // The folders of the whole tree share the visited set and the budget, so that folders referred to from several
        // places do not multiply the work
        var current = Dictionary.Get<PdfDictionary>(ChildName);
        while (current is not null && _tree.Visited.Count < PdfPortfolio.MaxFolders && _tree.Visited.Add(current))
        {
            result.Add(new PdfPortfolioFolder(current, Depth + 1, _tree));
            current = current.Get<PdfDictionary>(PdfNames.Next);
        }

        return result;
    }

    private sealed class FolderTree
    {
        public HashSet<PdfDictionary> Visited { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
