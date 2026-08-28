using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

internal static class AdfJsonReader
{
    public static AdfDocument ReadDocument(JsonElement element)
    {
        if (element.ValueKind is not JsonValueKind.Object)
            throw new AdfException("The root of an ADF document must be a JSON object");

        if (!element.TryGetProperty("type", out var type) || type.ValueKind is not JsonValueKind.String || type.GetString() != "doc")
            throw new AdfException("The root of an ADF document must have a 'type' property set to 'doc'");

        var version = 1;
        if (element.TryGetProperty("version", out var versionElement) && versionElement.TryGetInt32(out var parsedVersion))
        {
            version = parsedVersion;
        }

        return new AdfDocument
        {
            Version = version,
            Content = ReadNodes(element),
        };
    }

    private static List<AdfNode> ReadNodes(JsonElement parent)
    {
        if (!parent.TryGetProperty("content", out var content) || content.ValueKind is not JsonValueKind.Array)
            return [];

        var length = content.GetArrayLength();
        if (length == 0)
            return [];

        var result = new List<AdfNode>(length);
        foreach (var child in content.EnumerateArray())
        {
            if (child.ValueKind is JsonValueKind.Object)
            {
                result.Add(ReadNode(child));
            }
        }

        return result;
    }

    public static AdfNode ReadNode(JsonElement element)
    {
        var typeName = element.TryGetProperty("type", out var type) && type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;

        var attrs = element.TryGetProperty("attrs", out var a) && a.ValueKind is JsonValueKind.Object ? a : default;
        var node = Create(typeName, element, attrs);
        return node;
    }

    private static AdfNode Create(string? typeName, JsonElement element, JsonElement attrs)
    {
        var localId = attrs.AttrString("localId");
        var marks = ReadMarks(element);

        return typeName switch
        {
            "paragraph" => new AdfParagraph { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "heading" => new AdfHeading { Level = attrs.AttrInt32("level") ?? 1, LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "blockquote" => new AdfBlockquote { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "codeBlock" => new AdfCodeBlock { Language = attrs.AttrString("language"), LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "rule" => new AdfRule { LocalId = localId, Marks = marks },
            "bulletList" => new AdfBulletList { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "orderedList" => new AdfOrderedList { Order = attrs.AttrInt32("order"), LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "listItem" => new AdfListItem { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "panel" => new AdfPanel
            {
                PanelType = ParsePanelType(attrs.AttrString("panelType")),
                PanelIcon = attrs.AttrString("panelIcon"),
                PanelIconText = attrs.AttrString("panelIconText"),
                PanelColor = attrs.AttrString("panelColor"),
                LocalId = localId,
                Marks = marks,
                Content = ReadNodes(element),
            },
            "table" => new AdfTable
            {
                IsNumberColumnEnabled = attrs.AttrBoolean("isNumberColumnEnabled"),
                Layout = attrs.AttrString("layout"),
                Width = attrs.AttrDouble("width"),
                DisplayMode = attrs.AttrString("displayMode"),
                LocalId = localId,
                Marks = marks,
                Content = ReadNodes(element),
            },
            "tableRow" => new AdfTableRow { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "tableCell" => new AdfTableCell
            {
                ColSpan = attrs.AttrInt32("colspan"),
                RowSpan = attrs.AttrInt32("rowspan"),
                Background = attrs.AttrString("background"),
                VerticalAlignment = attrs.AttrString("valign"),
                LocalId = localId,
                Marks = marks,
                Content = ReadNodes(element),
            },
            "tableHeader" => new AdfTableHeader
            {
                ColSpan = attrs.AttrInt32("colspan"),
                RowSpan = attrs.AttrInt32("rowspan"),
                Background = attrs.AttrString("background"),
                VerticalAlignment = attrs.AttrString("valign"),
                LocalId = localId,
                Marks = marks,
                Content = ReadNodes(element),
            },
            "expand" => new AdfExpand { Title = attrs.AttrString("title"), LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "nestedExpand" => new AdfNestedExpand { Title = attrs.AttrString("title"), LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "taskList" => new AdfTaskList { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "taskItem" => new AdfTaskItem { State = ParseTaskState(attrs.AttrString("state")), LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "decisionList" => new AdfDecisionList { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "decisionItem" => new AdfDecisionItem { State = attrs.AttrString("state"), LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "mediaGroup" => new AdfMediaGroup { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "mediaSingle" => new AdfMediaSingle
            {
                Layout = attrs.AttrString("layout"),
                Width = attrs.AttrDouble("width"),
                WidthType = attrs.AttrString("widthType"),
                LocalId = localId,
                Marks = marks,
                Content = ReadNodes(element),
            },
            "media" => new AdfMedia
            {
                Type = ParseMediaType(attrs.AttrString("type")),
                Id = attrs.AttrString("id"),
                Collection = attrs.AttrString("collection"),
                Url = attrs.AttrString("url"),
                Alt = attrs.AttrString("alt"),
                Width = attrs.AttrDouble("width"),
                Height = attrs.AttrDouble("height"),
                OccurrenceKey = attrs.AttrString("occurrenceKey"),
                LocalId = localId,
                Marks = marks,
            },
            "mediaInline" => new AdfMediaInline
            {
                Type = ParseMediaType(attrs.AttrString("type")),
                Id = attrs.AttrString("id"),
                Collection = attrs.AttrString("collection"),
                Alt = attrs.AttrString("alt"),
                Width = attrs.AttrDouble("width"),
                Height = attrs.AttrDouble("height"),
                OccurrenceKey = attrs.AttrString("occurrenceKey"),
                LocalId = localId,
                Marks = marks,
            },
            "caption" => new AdfCaption { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "layoutSection" => new AdfLayoutSection { LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "layoutColumn" => new AdfLayoutColumn { Width = attrs.AttrDouble("width"), LocalId = localId, Marks = marks, Content = ReadNodes(element) },
            "blockCard" => new AdfBlockCard
            {
                Url = attrs.AttrString("url"),
                Data = attrs.AttrElement("data"),
                Datasource = attrs.AttrElement("datasource"),
                Layout = attrs.AttrString("layout"),
                Width = attrs.AttrDouble("width"),
                LocalId = localId,
                Marks = marks,
            },
            "embedCard" => new AdfEmbedCard
            {
                Url = attrs.AttrString("url") ?? "",
                Layout = attrs.AttrString("layout"),
                Width = attrs.AttrDouble("width"),
                LocalId = localId,
                Marks = marks,
            },
            "inlineCard" => new AdfInlineCard { Url = attrs.AttrString("url"), Data = attrs.AttrElement("data"), LocalId = localId, Marks = marks },
            "extension" => new AdfExtension
            {
                ExtensionKey = attrs.AttrString("extensionKey") ?? "",
                ExtensionType = attrs.AttrString("extensionType") ?? "",
                Text = attrs.AttrString("text"),
                Layout = attrs.AttrString("layout"),
                Parameters = attrs.AttrElement("parameters"),
                LocalId = localId,
                Marks = marks,
            },
            "bodiedExtension" => new AdfBodiedExtension
            {
                ExtensionKey = attrs.AttrString("extensionKey") ?? "",
                ExtensionType = attrs.AttrString("extensionType") ?? "",
                Text = attrs.AttrString("text"),
                Layout = attrs.AttrString("layout"),
                Parameters = attrs.AttrElement("parameters"),
                LocalId = localId,
                Marks = marks,
                Content = ReadNodes(element),
            },
            "inlineExtension" => new AdfInlineExtension
            {
                ExtensionKey = attrs.AttrString("extensionKey") ?? "",
                ExtensionType = attrs.AttrString("extensionType") ?? "",
                Text = attrs.AttrString("text"),
                Parameters = attrs.AttrElement("parameters"),
                LocalId = localId,
                Marks = marks,
            },
            "text" => new AdfText
            {
                Text = element.AttrString("text") ?? "",
                LocalId = localId,
                Marks = marks,
            },
            "hardBreak" => new AdfHardBreak { LocalId = localId, Marks = marks },
            "emoji" => new AdfEmoji
            {
                ShortName = attrs.AttrString("shortName") ?? "",
                Id = attrs.AttrString("id"),
                Text = attrs.AttrString("text"),
                Fallback = attrs.AttrString("fallback"),
                LocalId = localId,
                Marks = marks,
            },
            "mention" => new AdfMention
            {
                Id = attrs.AttrString("id") ?? "",
                Text = attrs.AttrString("text"),
                AccessLevel = attrs.AttrString("accessLevel"),
                UserType = attrs.AttrString("userType"),
                LocalId = localId,
                Marks = marks,
            },
            "date" => new AdfDate { Timestamp = attrs.AttrString("timestamp") ?? "", LocalId = localId, Marks = marks },
            "status" => new AdfStatus { Text = attrs.AttrString("text") ?? "", Color = attrs.AttrString("color"), Style = attrs.AttrString("style"), LocalId = localId, Marks = marks },
            "placeholder" => new AdfPlaceholder { Text = attrs.AttrString("text") ?? "", LocalId = localId, Marks = marks },
            _ => new AdfUnknownNode
            {
                TypeName = typeName ?? "",
                RawJson = element.Clone(),
                LocalId = localId,
                Marks = marks,
                Content = ReadNodes(element),
            },
        };
    }

    private static List<AdfMark> ReadMarks(JsonElement element)
    {
        if (!element.TryGetProperty("marks", out var marks) || marks.ValueKind is not JsonValueKind.Array)
            return [];

        var length = marks.GetArrayLength();
        if (length == 0)
            return [];

        var result = new List<AdfMark>(length);
        foreach (var mark in marks.EnumerateArray())
        {
            if (mark.ValueKind is JsonValueKind.Object)
            {
                result.Add(ReadMark(mark));
            }
        }

        return result;
    }

    private static AdfMark ReadMark(JsonElement element)
    {
        var typeName = element.TryGetProperty("type", out var type) && type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;

        var attrs = element.TryGetProperty("attrs", out var a) && a.ValueKind is JsonValueKind.Object ? a : default;

        return typeName switch
        {
            "strong" => new AdfStrongMark(),
            "em" => new AdfEmphasisMark(),
            "code" => new AdfCodeMark(),
            "strike" => new AdfStrikeMark(),
            "underline" => new AdfUnderlineMark(),
            "subsup" => new AdfSubSupMark { Type = attrs.AttrString("type") is "sub" ? AdfSubSupType.Subscript : AdfSubSupType.Superscript },
            "link" => new AdfLinkMark
            {
                Href = attrs.AttrString("href") ?? "",
                Title = attrs.AttrString("title"),
                Id = attrs.AttrString("id"),
                Collection = attrs.AttrString("collection"),
                OccurrenceKey = attrs.AttrString("occurrenceKey"),
            },
            "textColor" => new AdfTextColorMark { Color = attrs.AttrString("color") ?? "" },
            "backgroundColor" => new AdfBackgroundColorMark { Color = attrs.AttrString("color") ?? "" },
            "alignment" => new AdfAlignmentMark { Align = attrs.AttrString("align") ?? "" },
            "indentation" => new AdfIndentationMark { Level = attrs.AttrInt32("level") ?? 1 },
            "annotation" => new AdfAnnotationMark { Id = attrs.AttrString("id") ?? "", AnnotationType = attrs.AttrString("annotationType") },
            "border" => new AdfBorderMark { Size = attrs.AttrInt32("size") ?? 1, Color = attrs.AttrString("color") },
            "breakout" => new AdfBreakoutMark { Mode = attrs.AttrString("mode") ?? "", Width = attrs.AttrDouble("width") },
            _ => new AdfUnknownMark { TypeName = typeName ?? "", RawJson = element.Clone() },
        };
    }

    private static AdfPanelType ParsePanelType(string? value) => value switch
    {
        "info" => AdfPanelType.Info,
        "note" => AdfPanelType.Note,
        "tip" => AdfPanelType.Tip,
        "warning" => AdfPanelType.Warning,
        "error" => AdfPanelType.Error,
        "success" => AdfPanelType.Success,
        "custom" => AdfPanelType.Custom,
        _ => AdfPanelType.Unknown,
    };

    private static AdfTaskState ParseTaskState(string? value) => value is "DONE" ? AdfTaskState.Done : AdfTaskState.ToDo;

    private static AdfMediaType ParseMediaType(string? value) => value switch
    {
        "file" => AdfMediaType.File,
        "link" => AdfMediaType.Link,
        "external" => AdfMediaType.External,
        "image" => AdfMediaType.Image,
        _ => AdfMediaType.Unknown,
    };
}
