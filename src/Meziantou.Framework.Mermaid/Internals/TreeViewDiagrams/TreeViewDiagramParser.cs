namespace Meziantou.Framework.Mermaid.Internals.TreeViewDiagrams;

/// <summary>
/// Parses the <c>treeView-beta</c> diagrams of Mermaid. The depth of a node comes from its indentation, or, when the
/// tree is drawn with box-drawing characters (<c>├──</c>, <c>└──</c>, <c>│</c>), from the column of its branch, as the
/// preprocessor of Mermaid computes it. Unlike Mermaid, <c>%%</c> starts a comment anywhere outside a quoted name.
/// </summary>
internal sealed class TreeViewDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly TreeViewDiagram _diagram = new();
    private int _nodeCount;

    private TreeViewDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static TreeViewDiagram Parse(ParseContext context, int position) => new TreeViewDiagramParser(context, position).Parse();

    private TreeViewDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("treeView-beta"))
            throw _scanner.Error(keywordStart, "'treeView-beta' is expected.");

        var text = _scanner.Text;
        var keywordLineEnd = LineEnd(text, _scanner.Position);
        if (!IsBlank(text, _scanner.Position, keywordLineEnd))
            throw _scanner.Error("The nodes start on the line after 'treeView-beta'.");

        ApplyConfiguration();
        var lines = new List<(int Start, int End)>();
        for (var start = keywordLineEnd + 1; start <= text.Length; start = LineEnd(text, start) + 1)
        {
            lines.Add((start, LineEnd(text, start)));
        }

        var boxMode = IsBoxDrawing(text, lines, out var segmentWidth);
        var stack = new List<(int Level, IList<TreeViewNode> Children)> { (-1, _diagram.Nodes) };
        var seenNode = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var (start, end) = lines[index];
            var line = text[start..end];
            if (IsBlank(text, start, end) || (boxMode && line.Trim().Length is 0))
                continue;

            if (IsMetadataLine(line))
            {
                if (seenNode)
                    throw _scanner.Error(start, "The title and the accessibility statements come before the nodes.");

                index = ParseMetadata(lines, index);
                continue;
            }

            // A line that holds only the trunks of the tree draws nothing
            if (boxMode && IsDecoration(line, allowBranches: false))
                continue;

            var (level, content, contentOffset) = boxMode ? ReadBoxLine(line, start, segmentWidth) : ReadIndentedLine(line, start);
            if (content is null)
                continue;

            seenNode = true;
            var node = ParseNode(content, contentOffset);
            while (level <= stack[^1].Level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (stack.Count > _context.Options.MaxDepth)
                throw _scanner.Error(start, $"The nodes are nested deeper than {_context.Options.MaxDepth} levels.");

            if (++_nodeCount > _context.Options.MaxNodes)
                throw _scanner.Error(start, $"The tree has more than {_context.Options.MaxNodes} nodes.");

            stack[^1].Children.Add(node);
            stack.Add((level, node.Children));
        }

        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "treeView");
        var configuration = _context.Configuration;
        if (configuration.GetNumber("treeView", "rowIndent") is >= 0 and <= 1000 and var rowIndent)
        {
            _diagram.RowIndent = rowIndent;
        }

        if (configuration.GetNumber("treeView", "paddingX") is >= 0 and <= 1000 and var paddingX)
        {
            _diagram.PaddingX = paddingX;
        }

        if (configuration.GetNumber("treeView", "paddingY") is >= 0 and <= 1000 and var paddingY)
        {
            _diagram.PaddingY = paddingY;
        }

        if (configuration.GetNumber("treeView", "lineThickness") is >= 0 and <= 100 and var lineThickness)
        {
            _diagram.LineThickness = lineThickness;
        }

        if (configuration.GetBoolean("treeView", "showIcons") is { } showIcons)
        {
            _diagram.ShowIcons = showIcons;
        }

        if (configuration.GetString("treeView", "defaultIconPack") is { Length: > 0 } defaultIconPack)
        {
            _diagram.DefaultIconPack = defaultIconPack;
        }

        ReadIconMap("filenameIcons", _diagram.FileNameIcons);
        ReadIconMap("extensionIcons", _diagram.ExtensionIcons);

        // The colors and the font size are theme variables of the tree view: themeVariables.treeView
        if (configuration.GetNumber("themeVariables", "treeView", "labelFontSize") is > 0 and < 1000 and var fontSize)
        {
            _diagram.FontSize = fontSize;
        }

        _diagram.LabelColor = ReadColor("labelColor");
        _diagram.LineColor = ReadColor("lineColor");
        _diagram.IconColor = ReadColor("iconColor");
        _diagram.DescriptionColor = ReadColor("descriptionColor");
        _diagram.HighlightBackground = ReadColor("highlightBg");
        _diagram.HighlightBorderColor = ReadColor("highlightStroke");

        void ReadIconMap(string name, IDictionary<string, string> target)
        {
            if (configuration.GetMap("treeView", name) is not { } map)
                return;

            foreach (var (key, value) in map)
            {
                if (value is string icon && key.Length > 0)
                {
                    target[key] = icon;
                }
            }
        }

        SceneColor? ReadColor(string name)
        {
            if (configuration.GetString("themeVariables", "treeView", name) is not { } value)
                return null;

            if (SceneColor.TryParse(value, out var color))
                return color;

            _context.Warn(0, $"The value '{value}' of the theme variable 'treeView.{name}' is not a supported color.");
            return null;
        }
    }

    /// <summary>Parses a title or an accessibility statement; returns the index of its last line.</summary>
    private int ParseMetadata(List<(int Start, int End)> lines, int index)
    {
        var text = _scanner.Text;
        var (start, end) = lines[index];
        var position = start;
        while (position < end && text[position] is ' ' or '\t')
        {
            position++;
        }

        _scanner.Position = position;
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
        {
            // A description block can span several lines; what follows its closing brace must be blank
            var last = index;
            while (last + 1 < lines.Count && lines[last].End < _scanner.Position)
            {
                last++;
            }

            if (!IsBlank(text, _scanner.Position, lines[last].End))
                throw _scanner.Error($"Unexpected '{_scanner.Describe()}' after the accessibility statement.");

            return last;
        }

        // title, then the text up to a comment
        position += "title".Length;
        var titleEnd = text.IndexOf("%%", position, end - position, StringComparison.Ordinal);
        var title = text[position..(titleEnd < 0 ? end : titleEnd)].Trim();
        _diagram.Title = title.Length is 0 ? null : title;
        return index;
    }

    /// <summary>Reads a line of a tree drawn with indentation: its depth is the number of its leading spaces and tabs.</summary>
    private static (int Level, string? Content, int Offset) ReadIndentedLine(string line, int start)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] is ' ' or '\t')
        {
            indent++;
        }

        return (indent, line[indent..], start + indent);
    }

    /// <summary>
    /// Reads a line of a tree drawn with box-drawing characters, as Mermaid's preprocessor: the depth comes from the column
    /// of the branch, the tabs being 4 spaces; a line without branch is at the top level.
    /// </summary>
    private (int Level, string? Content, int Offset) ReadBoxLine(string line, int start, int segmentWidth)
    {
        var normalized = line.Replace("\t", "    ", StringComparison.Ordinal);
        var branch = normalized.AsSpan().IndexOfAny("└┗├┣");
        if (branch >= 0)
        {
            var depth = (int)Math.Round(branch / (double)segmentWidth, MidpointRounding.AwayFromZero) + 1;
            var position = branch + 1;
            while (position < normalized.Length && normalized[position] is '─' or '━')
            {
                position++;
            }

            while (position < normalized.Length && normalized[position] == ' ')
            {
                position++;
            }

            // Unlike Mermaid, the tabs of the name are kept: the column only maps back to the line
            var rawPosition = 0;
            for (var column = 0; column < position; rawPosition++)
            {
                column += line[rawPosition] == '\t' ? 4 : 1;
            }

            var content = line[rawPosition..].TrimEnd(' ', '\t');
            if (content.Length is 0)
                throw _scanner.Error(start, "The node has no name after its branch.");

            return (4 * depth, content, start + rawPosition);
        }

        if (IsDecoration(normalized, allowBranches: true))
            return (0, null, start);

        if (normalized.AsSpan().IndexOfAny("─━│┃") >= 0)
            return ReadIndentedLine(line, start);

        if (normalized.Length > 0 && char.IsWhiteSpace(normalized[0]))
            throw _scanner.Error(start, "A tree drawn with box-drawing characters indents its nodes with '├──' or '└──', not with spaces.");

        return (0, line, start);
    }

    /// <summary>Indicates whether a tree is drawn with box-drawing characters, and the number of columns of a level.</summary>
    private static bool IsBoxDrawing(string text, List<(int Start, int End)> lines, out int segmentWidth)
    {
        segmentWidth = 4;
        var boxMode = false;
        var widthFound = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var (start, end) = lines[index];
            var line = text[start..end];
            if (IsMetadataLine(line))
            {
                // Unlike Mermaid, the lines of an accDescr block are not nodes, and do not choose the syntax
                if (line.TrimStart().StartsWith("accDescr", StringComparison.Ordinal) && !line.Contains(':', StringComparison.Ordinal))
                {
                    while (index < lines.Count && !text.AsSpan(lines[index].Start, lines[index].End - lines[index].Start).Contains('}'))
                    {
                        index++;
                    }
                }

                continue;
            }

            if (line.Trim().Length is 0 || IsDecoration(line, allowBranches: false))
                continue;

            var normalized = line.Replace("\t", "    ", StringComparison.Ordinal);
            boxMode |= normalized.AsSpan().IndexOfAny("─━│┃└┗├┣") >= 0;
            var branch = normalized.AsSpan().IndexOfAny("└┗├┣");
            if (!widthFound && branch > 0)
            {
                segmentWidth = branch;
                widthFound = true;
            }
        }

        return boxMode;
    }

    /// <summary>Indicates whether a line holds only white space and trunks (and branches and dashes when allowed).</summary>
    private static bool IsDecoration(string line, bool allowBranches)
    {
        if (line.Length is 0)
            return false;

        foreach (var c in line)
        {
            if (!(char.IsWhiteSpace(c) || c is '│' or '┃' || (allowBranches && c is '─' or '━' or '└' or '┗' or '├' or '┣')))
                return false;
        }

        return true;
    }

    /// <summary>Indicates whether a line is a title or an accessibility statement.</summary>
    internal static bool IsMetadataLine(string line)
    {
        var text = line.AsSpan().TrimStart();
        if (text.StartsWith("title", StringComparison.Ordinal))
            return text.Length == 5 || text[5] is ' ' or '\t';

        foreach (var keyword in (ReadOnlySpan<string>)["accTitle", "accDescr"])
        {
            if (text.StartsWith(keyword, StringComparison.Ordinal))
            {
                var rest = text[keyword.Length..].TrimStart(" \t");
                return rest.Length > 0 && (rest[0] == ':' || (keyword == "accDescr" && rest[0] == '{'));
            }
        }

        return false;
    }

    /// <summary>Parses a node: its quoted or bare name, then its annotations: <c>:::class</c>, <c>icon(name)</c> and <c>## description</c>.</summary>
    private TreeViewNode ParseNode(string content, int offset)
    {
        string name;
        int position;
        if (content[0] is '"' or '\'')
        {
            var close = content.IndexOfOrdinal(content[0], 1);
            if (close < 0)
                throw _scanner.Error(offset, "The name is not closed by its quote.");

            name = content[1..close];
            if (name.Contains("%%", StringComparison.Ordinal))
                throw _scanner.Error(offset, "A name cannot contain '%%'.");

            position = close + 1;
        }
        else
        {
            if (content.StartsWith(":::", StringComparison.Ordinal) || content.StartsWith("icon(", StringComparison.Ordinal) || content.StartsWith("##", StringComparison.Ordinal))
                throw _scanner.Error(offset, $"Unexpected '{content.Split(' ')[0]}': a name is expected before the annotations.");

            position = 0;
            while (position < content.Length && !IsCommentAt(content, position) && AnnotationLength(content, position, out var kind) is 0 && kind is not AnnotationKind.Invalid)
            {
                position++;
            }

            name = content[..position].TrimEnd(' ', '\t');
        }

        var node = new TreeViewNode(name);
        if (name.EndsWith("/", StringComparison.Ordinal))
        {
            node.Name = name[..^1];
            node.IsDirectory = true;
        }

        while (position < content.Length)
        {
            var rest = content.AsSpan(position);
            var blank = rest.TrimStart(" \t");
            if (blank.Length is 0 || blank.StartsWith("%%", StringComparison.Ordinal))
                break;

            var length = AnnotationLength(content, position, out var kind);
            if (length is 0)
                throw _scanner.Error(offset + position, $"Unexpected '{rest.Trim().ToString()}': an annotation is expected (:::class, icon(name) or ## description).");

            var annotation = content.AsSpan(position, length).TrimStart(" \t");
            switch (kind)
            {
                case AnnotationKind.Class:
                    node.CssClass = annotation[3..].TrimStart(" \t").ToString();
                    break;
                case AnnotationKind.Icon:
                    var icon = annotation[5..^1].ToString();
                    node.Icon = icon.Length is 0 ? "none" : icon;
                    break;
                default:
                    var descriptionEnd = content.IndexOf("%%", position, StringComparison.Ordinal);
                    var description = content[(position + (length - annotation.Length) + 2)..(descriptionEnd < 0 ? content.Length : descriptionEnd)].Trim();
                    node.Description = description.Length is 0 ? null : description;
                    return node;
            }

            position += length;
        }

        return node;
    }

    private static bool IsCommentAt(string text, int position) => position + 1 < text.Length && text[position] == '%' && text[position + 1] == '%';

    /// <summary>Returns the length of the annotation at a position, with its leading white space, or 0 when there is none.</summary>
    internal static int AnnotationLength(string text, int position, out AnnotationKind kind)
    {
        kind = AnnotationKind.None;
        var i = position;
        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        if (i == position)
            return 0;

        if (string.CompareOrdinal(text, i, ":::", 0, 3) == 0)
        {
            var j = i + 3;
            while (j < text.Length && text[j] is ' ' or '\t')
            {
                j++;
            }

            if (j >= text.Length || !(char.IsAsciiLetter(text[j]) || text[j] == '_'))
                return 0;

            j++;
            while (j < text.Length && (char.IsAsciiLetterOrDigit(text[j]) || text[j] is '_' or '-'))
            {
                j++;
            }

            kind = AnnotationKind.Class;
            return j - position;
        }

        if (string.CompareOrdinal(text, i, "icon(", 0, 5) == 0)
        {
            var j = i + 5;
            while (j < text.Length && text[j] != ')' && text[j] != '\n')
            {
                j++;
            }

            if (j >= text.Length || !IsValidIcon(text[(i + 5)..j]))
            {
                // A bare name stops before " icon(" even when the annotation is not valid, as in Mermaid, and the annotation is then an error
                kind = AnnotationKind.Invalid;
                return 0;
            }

            kind = AnnotationKind.Icon;
            return j + 1 - position;
        }

        if (string.CompareOrdinal(text, i, "##", 0, 2) == 0)
        {
            kind = AnnotationKind.Description;
            return text.Length - position;
        }

        return 0;
    }

    /// <summary>Indicates whether a name is an icon of Mermaid: an optional pack, then a name, of letters, digits, <c>_</c> and <c>-</c>.</summary>
    internal static bool IsValidIcon(string icon)
    {
        var colon = icon.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? IsWord(icon, allowEmpty: true) : IsWord(icon[..colon], allowEmpty: true) && IsWord(icon[(colon + 1)..], allowEmpty: false);

        static bool IsWord(string value, bool allowEmpty) => (allowEmpty || value.Length > 0) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    internal static bool IsValidClass(string name) => name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static int LineEnd(string text, int start)
    {
        var end = text.IndexOfOrdinal('\n', start);
        return end < 0 ? text.Length : end;
    }

    private static bool IsBlank(string text, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (text[i] is not (' ' or '\t'))
                return false;
        }

        return true;
    }

    internal enum AnnotationKind
    {
        None,
        Class,
        Icon,
        Description,
        Invalid,
    }
}
