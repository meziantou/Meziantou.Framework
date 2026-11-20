using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Tags an untagged document (ISO 32000-2, 14.8): the structure elements of the layout of its pages
/// (<see cref="LayoutAnalyzer"/>), and its content rewritten with marked content: each operator that shows text or an
/// image in a marked-content sequence of its element (14.7.5.2), the shapes, the rules and the headers and footers as
/// artifacts (14.8.2.2).
/// </summary>
internal static class AutoTagger
{
    private static readonly HashSet<string> TextOperators = new(StringComparer.Ordinal) { "Tj", "TJ", "'", "\"" };
    private static readonly HashSet<string> PathConstruction = new(StringComparer.Ordinal) { "m", "l", "c", "v", "y", "h", "re", "W", "W*" };
    private static readonly HashSet<string> PathPainting = new(StringComparer.Ordinal) { "S", "s", "f", "F", "f*", "B", "B*", "b", "b*" };

    public static PdfAutoTagResult Tag(PdfDocument document, PdfAutoTagOptions options)
    {
        if (document.StructureTree.Exists || document.Catalog.Get<PdfDictionary>(PdfName.Create("MarkInfo"))?.GetBoolean(PdfName.Create("Marked")) is true)
            throw new InvalidOperationException("The document is already tagged");

        var result = new PdfAutoTagResult();
        var pages = document.Pages.ToList();
        var layout = LayoutAnalyzer.Analyze(pages, options.Layout);
        var root = document.StructureTree.AddElement("Document");
        foreach (var page in layout)
        {
            var elements = new Dictionary<int, PdfStructureElement>();
            var pagination = new HashSet<int>();
            foreach (var node in page.Nodes)
            {
                AddNode(root, node, elements, pagination, options, result);
            }

            Rewrite(document, page.Page, elements, pagination, result);
        }

        if (options.Language is { } language && document.Language is null)
        {
            document.Language = language;
        }

        return result;
    }

    private static void AddNode(PdfStructureElement parent, PdfLayoutNode node, Dictionary<int, PdfStructureElement> elements, HashSet<int> pagination, PdfAutoTagOptions options, PdfAutoTagResult result)
    {
        switch (node.Kind)
        {
            case PdfLayoutNodeKind.Header or PdfLayoutNodeKind.Footer:
                pagination.UnionWith(node.Operations);
                break;
            case PdfLayoutNodeKind.Heading:
                Map(parent.AddElement("H" + Math.Clamp(node.Level, 1, 6).ToString(System.Globalization.CultureInfo.InvariantCulture)), node.Operations, elements);
                result.Headings++;
                break;
            case PdfLayoutNodeKind.Paragraph:
                Map(parent.AddElement("P"), node.Operations, elements);
                result.Paragraphs++;
                break;
            case PdfLayoutNodeKind.List:
                var list = parent.AddElement("L");
                foreach (var item in node.Children)
                {
                    Map(list.AddElement("LI").AddElement("LBody"), item.Operations, elements);
                }

                result.Lists++;
                break;
            case PdfLayoutNodeKind.Table:
                var table = parent.AddElement("Table");
                var rowCount = node.Children.Count == 0 ? 0 : node.Children.Max(cell => cell.Row) + 1;
                var header = options.FirstTableRowIsHeader && rowCount > 1;
                foreach (var row in node.Children.GroupBy(cell => cell.Row).OrderBy(group => group.Key))
                {
                    var rowElement = table.AddElement("TR");
                    foreach (var cell in row.OrderBy(cell => cell.Column))
                    {
                        var isHeader = header && row.Key == 0;
                        var cellElement = rowElement.AddElement(isHeader ? "TH" : "TD");
                        if (isHeader)
                        {
                            cellElement.SetAttribute("Table", "Scope", PdfName.Create("Column"));
                        }

                        Map(cellElement, cell.Operations, elements);
                    }
                }

                result.Tables++;
                break;
            case PdfLayoutNodeKind.Figure:
                var figure = parent.AddElement("Figure");
                if (node.Image is { } image && options.FigureAlternateText?.Invoke(image) is { } alternate)
                {
                    figure.AlternateDescription = alternate;
                }
                else
                {
                    result.FiguresWithoutAlternateText++;
                }

                figure.SetAttribute("Layout", "BBox", node.Bounds.ToArray());
                Map(figure, node.Operations, elements);
                result.Figures++;
                break;
        }
    }

    /// <summary>Maps operators to an element: an operator that two nodes share is in the first one.</summary>
    private static void Map(PdfStructureElement element, IEnumerable<int> operations, Dictionary<int, PdfStructureElement> elements)
    {
        foreach (var operation in operations)
        {
            elements.TryAdd(operation, element);
        }
    }

    /// <summary>
    /// Rewrites the content of a page: the operators that show text, paint an image or a form are each in a marked-content
    /// sequence of their element, or are artifacts; a path is an artifact from its first construction operator to its
    /// painting operator, since marked content cannot split a path object (8.5.1).
    /// </summary>
    private static void Rewrite(PdfDocument document, PdfPage page, Dictionary<int, PdfStructureElement> elements, HashSet<int> pagination, PdfAutoTagResult result)
    {
        var data = page.GetContentData();
        var reader = new ContentStreamReader(data);
        var operands = new List<PdfObject>();
        var output = new List<(string Name, PdfObject[] Operands, byte[]? Data)>();
        var pathStart = -1;
        var index = -1;
        var tree = document.StructureTree;
        while (reader.TryRead(operands, out var name))
        {
            index++;
            (string, PdfObject[], byte[]?) operation = name == "BI" && operands is [PdfDictionary inline, PdfString imageData]
                ? (name, [inline], imageData.Bytes.ToArray())
                : (name, [.. operands], null);
            if (PathConstruction.Contains(name))
            {
                if (pathStart < 0)
                {
                    pathStart = output.Count;
                }

                output.Add(operation);
                continue;
            }

            if (PathPainting.Contains(name))
            {
                output.Insert(pathStart >= 0 ? pathStart : output.Count, ("BMC", [PdfName.Create("Artifact")], null));
                output.Add(operation);
                output.Add(("EMC", [], null));
                pathStart = -1;
                result.Artifacts++;
                continue;
            }

            if (name == "n")
            {
                pathStart = -1;
                output.Add(operation);
                continue;
            }

            var paints = TextOperators.Contains(name) || name is "Do" or "BI" or "sh";
            if (!paints)
            {
                output.Add(operation);
                continue;
            }

            if (elements.TryGetValue(index, out var element))
            {
                var mcid = tree.AddMarkedContent(element, page);
                output.Add(("BDC", [PdfName.Create(element.Type), new PdfDictionary { [PdfName.Create("MCID")] = PdfInteger.Get(mcid) }], null));
            }
            else if (pagination.Contains(index))
            {
                output.Add(("BDC", [PdfNames.Artifact, new PdfDictionary { [PdfNames.Type] = PdfName.Create("Pagination") }], null));
                result.Artifacts++;
            }
            else
            {
                output.Add(("BMC", [PdfNames.Artifact], null));
                result.Artifacts++;
            }

            output.Add(operation);
            output.Add(("EMC", [], null));
        }

        var writer = new Writing.PdfByteWriter(data.Length + (output.Count * 16));
        foreach (var (name, values, imageData) in output)
        {
            ContentStreamWriter.Write(writer, name, values, imageData);
        }

        page.Dictionary[PdfNames.Contents] = document.Objects.Add(new PdfStream(new PdfDictionary(), writer.ToArray()));
    }
}
