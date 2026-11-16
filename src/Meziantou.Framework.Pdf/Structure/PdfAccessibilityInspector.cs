using System.Globalization;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Inspects an existing document for the accessibility issues that can be found automatically, as PDF/UA (ISO 14289)
/// and Tagged PDF (ISO 32000-2, 14.8) define them: the structure tree, the language and the title, the content that is
/// not tagged, the figures without alternate text, the annotations, the reading order. The document is not changed.
/// </summary>
/// <remarks>
/// A document without issues may still need a review: the inspector does not judge whether an alternate text describes
/// its figure, or whether the structure follows the meaning of the content. Validate the documents that must conform
/// to PDF/UA with a validator such as veraPDF.
/// </remarks>
public static class PdfAccessibilityInspector
{
    private const int MaxElements = 100_000;
    private const int MaxDepth = 64;
    private const int MaxOperations = 2_000_000;

    private static readonly PdfName StructTreeRootName = PdfName.Create("StructTreeRoot");
    private static readonly PdfName MarkInfoName = PdfName.Create("MarkInfo");
    private static readonly PdfName MarkedName = PdfName.Create("Marked");
    private static readonly PdfName ViewerPreferencesName = PdfName.Create("ViewerPreferences");
    private static readonly PdfName DisplayDocTitleName = PdfName.Create("DisplayDocTitle");
    private static readonly PdfName RoleMapName = PdfName.Create("RoleMap");
    private static readonly PdfName KName = PdfName.Create("K");
    private static readonly PdfName PgName = PdfName.Create("Pg");
    private static readonly PdfName McidName = PdfName.Create("MCID");
    private static readonly PdfName AltName = PdfName.Create("Alt");
    private static readonly PdfName ActualTextName = PdfName.Create("ActualText");
    private static readonly PdfName ObjName = PdfName.Create("Obj");
    private static readonly PdfName StructParentName = PdfName.Create("StructParent");
    private static readonly PdfName TabsName = PdfName.Create("Tabs");

    /// <summary>The standard structure types of PDF 1.7 and PDF 2.0 (14.8.4).</summary>
    private static readonly HashSet<string> StandardTypes = new(StringComparer.Ordinal)
    {
        "Document", "DocumentFragment", "Part", "Art", "Sect", "Div", "BlockQuote", "Caption", "TOC", "TOCI", "Index",
        "NonStruct", "Private", "Aside", "Title", "FENote", "Sub", "P", "H", "H1", "H2", "H3", "H4", "H5", "H6", "L", "LI",
        "Lbl", "LBody", "Table", "TR", "TH", "TD", "THead", "TBody", "TFoot", "Span", "Quote", "Note", "Reference",
        "BibEntry", "Code", "Link", "Annot", "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP", "Figure", "Formula", "Form",
        "Em", "Strong", "Artifact",
    };

    /// <summary>Inspects a document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The issues found.</returns>
    /// <exception cref="PdfException">The page tree of the document cannot be read.</exception>
    public static PdfAccessibilityReport Inspect(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<PdfAccessibilityIssue>();
        var catalog = document.Catalog;
        var root = catalog.GetResolved(StructTreeRootName) as PdfDictionary;
        if (root is null)
        {
            issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.MissingStructureTree, "The document has no structure tree: its content is not tagged"));
        }

        if ((catalog.GetResolved(MarkInfoName) as PdfDictionary)?.GetBoolean(MarkedName) is not true)
        {
            issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.NotMarkedAsTagged, "The catalog does not declare the document as tagged (/MarkInfo << /Marked true >>)"));
        }

        if (string.IsNullOrWhiteSpace(Try(() => document.Language)))
        {
            issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.MissingLanguage, "The document has no natural language (/Lang)"));
        }

        if (string.IsNullOrWhiteSpace(Try(() => document.Information.Title)) && !HasXmpTitle(document))
        {
            issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.MissingTitle, "The document has no title"));
        }

        if ((catalog.GetResolved(ViewerPreferencesName) as PdfDictionary)?.GetBoolean(DisplayDocTitleName) is not true)
        {
            issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.TitleNotDisplayed, "The viewer shows the file name instead of the title (/DisplayDocTitle is not true)"));
        }

        var pages = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        var index = 0;
        foreach (var page in document.Pages)
        {
            pages.TryAdd(page.Dictionary, index);
            index++;
        }

        var structure = new StructureWalker(pages, issues);
        if (root is not null)
        {
            structure.Walk(root);
        }

        index = 0;
        foreach (var page in document.Pages)
        {
            InspectPage(page, index, root is not null, structure.GetMarkedContent(index), issues);
            index++;
        }

        return new PdfAccessibilityReport(root is not null, issues);
    }

    private static void InspectPage(PdfPage page, int index, bool isTagged, List<int> structureOrder, List<PdfAccessibilityIssue> issues)
    {
        if (isTagged)
        {
            var (untagged, contentOrder) = ReadContent(page);
            if (untagged > 0)
            {
                issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.UntaggedContent, string.Create(CultureInfo.InvariantCulture, $"{untagged} painting operators are neither tagged nor artifacts"), index));
            }

            // The structure reads the marked content in the order the content draws it
            var positions = structureOrder.Select(mcid => contentOrder.IndexOf(mcid)).Where(position => position >= 0).ToList();
            for (var i = 1; i < positions.Count; i++)
            {
                if (positions[i] < positions[i - 1])
                {
                    issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.ReadingOrder, string.Create(CultureInfo.InvariantCulture, $"The structure reads the marked content {contentOrder[positions[i - 1]]} before {contentOrder[positions[i]]}, which the content draws first"), index));
                    break;
                }
            }
        }

        IReadOnlyList<PdfAnnotation> annotations;
        try
        {
            annotations = page.Annotations;
        }
        catch (PdfException)
        {
            return;
        }

        var visible = annotations.Where(annotation => annotation.Subtype is not "Popup" && !annotation.Flags.HasFlag(PdfAnnotationFlags.Hidden)).ToList();
        if (isTagged)
        {
            var untaggedAnnotations = visible.Count(annotation => !annotation.Dictionary.ContainsKey(StructParentName));
            if (untaggedAnnotations > 0)
            {
                issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.UntaggedAnnotation, string.Create(CultureInfo.InvariantCulture, $"{untaggedAnnotations} annotations are not in the structure tree"), index));
            }
        }

        if (visible.Count > 0 && page.Dictionary.GetName(TabsName)?.Value != "S")
        {
            issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.MissingTabOrder, "The page has annotations, and does not order them by the structure (/Tabs /S)", index));
        }
    }

    /// <summary>
    /// Reads the content of a page: the painting operators that are neither in a marked-content sequence with a marked
    /// content identifier nor in an artifact, and the marked content identifiers in the order of the content (14.7.5).
    /// </summary>
    private static (int Untagged, List<int> Order) ReadContent(PdfPage page)
    {
        var order = new List<int>();
        var untagged = 0;
        ReadOnlyMemory<byte> content;
        try
        {
            content = page.GetContentData();
        }
        catch (PdfException)
        {
            return (0, order);
        }

        var resources = page.Resources;
        var reader = new ContentStreamReader(content);
        var operands = new List<PdfObject>();
        var stack = new Stack<bool>();
        var operations = 0;
        while (reader.TryRead(operands, out var name) && operations++ < MaxOperations)
        {
            switch (name)
            {
                case "BMC":
                    stack.Push((stack.Count > 0 && stack.Peek()) || operands is [PdfName { Value: "Artifact" }, ..]);
                    break;
                case "BDC":
                    var covered = stack.Count > 0 && stack.Peek();
                    if (operands is [PdfName tag, var value, ..])
                    {
                        var properties = value switch
                        {
                            PdfDictionary dictionary => dictionary,
                            PdfName propertyName => Try(() => (resources?.GetResolved(PdfNames.Properties) as PdfDictionary)?.GetResolved(propertyName) as PdfDictionary),
                            _ => null,
                        };
                        if (tag.Value == "Artifact")
                        {
                            covered = true;
                        }
                        else if (properties?.GetResolved(McidName) is PdfNumber mcid)
                        {
                            covered = true;
                            order.Add((int)mcid.DoubleValue);
                        }
                    }

                    stack.Push(covered);
                    break;
                case "EMC":
                    stack.TryPop(out _);
                    break;
                case "Tj" or "TJ" or "'" or "\"" or "f" or "F" or "f*" or "S" or "s" or "B" or "B*" or "b" or "b*" or "Do" or "sh" or "BI":
                    if (stack.Count == 0 || !stack.Peek())
                    {
                        untagged++;
                    }

                    break;
            }
        }

        return (untagged, order);
    }

    private static bool HasXmpTitle(PdfDocument document)
    {
        var xmp = Try(document.GetXmpMetadata);
        if (xmp is null)
            return false;

        var text = System.Text.Encoding.UTF8.GetString(xmp);
        var start = text.IndexOf("<dc:title", StringComparison.Ordinal);
        if (start < 0)
            return false;

        var end = text.IndexOf("</dc:title>", start, StringComparison.Ordinal);
        if (end < 0)
            return false;

        // The text of the title: the content of its elements, without the tags
        var inner = text[start..end];
        var hasText = false;
        var inTag = false;
        foreach (var c in inner)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>')
            {
                inTag = false;
            }
            else if (!inTag && !char.IsWhiteSpace(c))
            {
                hasText = true;
                break;
            }
        }

        return hasText;
    }

    private static T? Try<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (PdfException)
        {
            return default;
        }
    }

    /// <summary>Walks the structure tree: the types of the elements, their alternate texts, the headings, and the marked content of each page in the order of the structure.</summary>
    private sealed class StructureWalker(Dictionary<PdfDictionary, int> pages, List<PdfAccessibilityIssue> issues)
    {
        private readonly Dictionary<int, List<int>> _markedContent = [];
        private readonly HashSet<PdfObject> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> _unknownTypes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _roleMap = new(StringComparer.Ordinal);
        private int _lastHeading;
        private int _elements;

        public List<int> GetMarkedContent(int pageIndex) => _markedContent.TryGetValue(pageIndex, out var list) ? list : [];

        public void Walk(PdfDictionary root)
        {
            if (root.GetResolved(RoleMapName) is PdfDictionary roleMap)
            {
                foreach (var (key, _) in roleMap)
                {
                    if (roleMap.GetResolved(key) is PdfName mapped)
                    {
                        _roleMap[key.Value] = mapped.Value;
                    }
                }
            }

            WalkKids(root.GetResolved(KName), page: null, depth: 0);
        }

        private void WalkKids(PdfObject? kids, int? page, int depth)
        {
            switch (kids)
            {
                case PdfArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        WalkKid(array.GetResolved(i), page, depth);
                    }

                    break;
                case not null:
                    WalkKid(kids, page, depth);
                    break;
            }
        }

        private void WalkKid(PdfObject? kid, int? page, int depth)
        {
            switch (kid)
            {
                case PdfNumber mcid when page is { } pageIndex:
                    AddMarkedContent(pageIndex, (int)mcid.DoubleValue);
                    break;

                case PdfDictionary dictionary when dictionary.IsType(PdfName.Create("MCR")):
                    if (dictionary.GetResolved(McidName) is PdfNumber reference && (GetPage(dictionary) ?? page) is { } referencePage)
                    {
                        AddMarkedContent(referencePage, (int)reference.DoubleValue);
                    }

                    break;

                case PdfDictionary dictionary when dictionary.IsType(PdfName.Create("OBJR")) || dictionary.ContainsKey(ObjName):
                    break;

                case PdfDictionary element when element.GetName(PdfNames.S) is { } type:
                    if (depth > MaxDepth || ++_elements > MaxElements || !_visited.Add(element))
                        return;

                    var elementPage = GetPage(element) ?? page;
                    CheckElement(element, type.Value, elementPage);
                    WalkKids(element.GetResolved(KName), elementPage, depth + 1);
                    break;
            }
        }

        private void CheckElement(PdfDictionary element, string type, int? page)
        {
            var standard = Resolve(type);
            if (standard is null)
            {
                if (_unknownTypes.Add(type))
                {
                    issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.UnknownStructureType, $"The structure type {type} is not a standard type, and the role map does not map it to one", page, type));
                }

                return;
            }

            if (standard is "Figure" or "Formula" && string.IsNullOrWhiteSpace(element.GetText(AltName)) && string.IsNullOrWhiteSpace(element.GetText(ActualTextName)))
            {
                issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.MissingAlternateText, $"A {standard} has no alternate text (/Alt)", page, type));
            }

            if (standard is ['H', >= '1' and <= '6'])
            {
                var level = standard[1] - '0';
                if (level > _lastHeading + 1)
                {
                    issues.Add(new PdfAccessibilityIssue(PdfAccessibilityIssueKind.SkippedHeadingLevel, string.Create(CultureInfo.InvariantCulture, $"An {standard} follows an {(_lastHeading == 0 ? "absence of heading" : "H" + _lastHeading.ToString(CultureInfo.InvariantCulture))}: heading levels are skipped"), page, type));
                }

                _lastHeading = level;
            }
        }

        /// <summary>Maps a type to a standard type through the role map (14.8.3); <see langword="null"/> when it maps to none.</summary>
        private string? Resolve(string type)
        {
            for (var i = 0; i < 16; i++)
            {
                if (StandardTypes.Contains(type))
                    return type;

                if (!_roleMap.TryGetValue(type, out var mapped))
                    return null;

                type = mapped;
            }

            return null;
        }

        private int? GetPage(PdfDictionary dictionary) => dictionary.GetResolved(PgName) is PdfDictionary page && pages.TryGetValue(page, out var index) ? index : null;

        private void AddMarkedContent(int page, int mcid)
        {
            if (!_markedContent.TryGetValue(page, out var list))
            {
                list = [];
                _markedContent.Add(page, list);
            }

            if (list.Count < MaxElements)
            {
                list.Add(mcid);
            }
        }
    }
}
