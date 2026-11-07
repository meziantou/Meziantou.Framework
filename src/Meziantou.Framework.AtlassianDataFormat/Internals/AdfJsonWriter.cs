using System.Text.Json;

namespace Meziantou.Framework.AtlassianDataFormat;

internal static class AdfJsonWriter
{
    public static void WriteDocument(Utf8JsonWriter writer, AdfDocument document)
    {
        writer.WriteStartObject();

        // Atlassian documents put "version" before "type" at the root.
        writer.WriteNumber("version", document.Version);
        writer.WriteString("type", "doc");
        WriteContent(writer, document.Content);
        writer.WriteEndObject();
    }

    public static void WriteNode(Utf8JsonWriter writer, AdfNode node)
    {
        if (node is AdfUnknownNode unknown)
        {
            unknown.RawJson.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", GetTypeName(node));
        WriteAttributes(writer, node);

        if (node is AdfText text)
        {
            writer.WriteString("text", text.Text);
        }

        WriteContent(writer, node.Content);
        WriteMarks(writer, node.Marks);
        writer.WriteEndObject();
    }

    private static void WriteContent(Utf8JsonWriter writer, IReadOnlyList<AdfNode> content)
    {
        if (content.Count == 0)
            return;

        writer.WriteStartArray("content");
        foreach (var child in content)
        {
            WriteNode(writer, child);
        }

        writer.WriteEndArray();
    }

    private static void WriteMarks(Utf8JsonWriter writer, IReadOnlyList<AdfMark> marks)
    {
        if (marks.Count == 0)
            return;

        writer.WriteStartArray("marks");
        foreach (var mark in marks)
        {
            WriteMark(writer, mark);
        }

        writer.WriteEndArray();
    }

    private static void WriteMark(Utf8JsonWriter writer, AdfMark mark)
    {
        if (mark is AdfUnknownMark unknown)
        {
            unknown.RawJson.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", GetTypeName(mark));

        var attrs = new AttributeScope(writer);
        switch (mark)
        {
            case AdfSubSupMark subsup:
                attrs.WriteString("type", subsup.Type is AdfSubSupType.Subscript ? "sub" : "sup");
                break;

            case AdfLinkMark link:
                attrs.WriteString("href", link.Href);
                attrs.WriteString("title", link.Title);
                attrs.WriteString("id", link.Id);
                attrs.WriteString("collection", link.Collection);
                attrs.WriteString("occurrenceKey", link.OccurrenceKey);
                break;

            case AdfTextColorMark textColor:
                attrs.WriteString("color", textColor.Color);
                break;

            case AdfBackgroundColorMark backgroundColor:
                attrs.WriteString("color", backgroundColor.Color);
                break;

            case AdfAlignmentMark alignment:
                attrs.WriteString("align", alignment.Align);
                break;

            case AdfIndentationMark indentation:
                attrs.WriteNumber("level", indentation.Level);
                break;

            case AdfAnnotationMark annotation:
                attrs.WriteString("id", annotation.Id);
                attrs.WriteString("annotationType", annotation.AnnotationType);
                break;

            case AdfBorderMark border:
                attrs.WriteNumber("size", border.Size);
                attrs.WriteString("color", border.Color);
                break;

            case AdfBreakoutMark breakout:
                attrs.WriteString("mode", breakout.Mode);
                attrs.WriteNumber("width", breakout.Width);
                break;
        }

        attrs.End();
        writer.WriteEndObject();
    }

    private static void WriteAttributes(Utf8JsonWriter writer, AdfNode node)
    {
        var attrs = new AttributeScope(writer);
        switch (node)
        {
            case AdfHeading heading:
                attrs.WriteNumber("level", heading.Level);
                break;

            case AdfCodeBlock codeBlock:
                attrs.WriteString("language", codeBlock.Language);
                break;

            case AdfOrderedList orderedList:
                attrs.WriteNumber("order", orderedList.Order);
                break;

            case AdfPanel panel:
                attrs.WriteString("panelType", GetPanelTypeName(panel.PanelType));
                attrs.WriteString("panelIcon", panel.PanelIcon);
                attrs.WriteString("panelIconText", panel.PanelIconText);
                attrs.WriteString("panelColor", panel.PanelColor);
                break;

            case AdfTable table:
                attrs.WriteBoolean("isNumberColumnEnabled", table.IsNumberColumnEnabled);
                attrs.WriteString("layout", table.Layout);
                attrs.WriteNumber("width", table.Width);
                attrs.WriteString("displayMode", table.DisplayMode);
                break;

            case AdfTableCell cell:
                attrs.WriteNumber("colspan", cell.ColSpan);
                attrs.WriteNumber("rowspan", cell.RowSpan);
                attrs.WriteString("background", cell.Background);
                attrs.WriteString("valign", cell.VerticalAlignment);
                break;

            case AdfTableHeader header:
                attrs.WriteNumber("colspan", header.ColSpan);
                attrs.WriteNumber("rowspan", header.RowSpan);
                attrs.WriteString("background", header.Background);
                attrs.WriteString("valign", header.VerticalAlignment);
                break;

            case AdfExpand expand:
                attrs.WriteString("title", expand.Title);
                break;

            case AdfNestedExpand nestedExpand:
                attrs.WriteString("title", nestedExpand.Title);
                break;

            case AdfTaskItem taskItem:
                attrs.WriteString("state", taskItem.State is AdfTaskState.Done ? "DONE" : "TODO");
                break;

            case AdfDecisionItem decisionItem:
                attrs.WriteString("state", decisionItem.State);
                break;

            case AdfMediaSingle mediaSingle:
                attrs.WriteString("layout", mediaSingle.Layout);
                attrs.WriteNumber("width", mediaSingle.Width);
                attrs.WriteString("widthType", mediaSingle.WidthType);
                break;

            case AdfMedia media:
                attrs.WriteString("type", GetMediaTypeName(media.Type));
                attrs.WriteString("id", media.Id);
                attrs.WriteString("collection", media.Collection);
                attrs.WriteString("url", media.Url);
                attrs.WriteString("alt", media.Alt);
                attrs.WriteNumber("width", media.Width);
                attrs.WriteNumber("height", media.Height);
                attrs.WriteString("occurrenceKey", media.OccurrenceKey);
                break;

            case AdfMediaInline mediaInline:
                attrs.WriteString("type", GetMediaTypeName(mediaInline.Type));
                attrs.WriteString("id", mediaInline.Id);
                attrs.WriteString("collection", mediaInline.Collection);
                attrs.WriteString("alt", mediaInline.Alt);
                attrs.WriteNumber("width", mediaInline.Width);
                attrs.WriteNumber("height", mediaInline.Height);
                attrs.WriteString("occurrenceKey", mediaInline.OccurrenceKey);
                break;

            case AdfLayoutColumn layoutColumn:
                attrs.WriteNumber("width", layoutColumn.Width);
                break;

            case AdfBlockCard blockCard:
                attrs.WriteString("url", blockCard.Url);
                attrs.WriteElement("data", blockCard.Data);
                attrs.WriteElement("datasource", blockCard.Datasource);
                attrs.WriteString("layout", blockCard.Layout);
                attrs.WriteNumber("width", blockCard.Width);
                break;

            case AdfEmbedCard embedCard:
                attrs.WriteString("url", embedCard.Url);
                attrs.WriteString("layout", embedCard.Layout);
                attrs.WriteNumber("width", embedCard.Width);
                break;

            case AdfInlineCard inlineCard:
                attrs.WriteString("url", inlineCard.Url);
                attrs.WriteElement("data", inlineCard.Data);
                break;

            case AdfExtension extension:
                attrs.WriteString("extensionKey", extension.ExtensionKey);
                attrs.WriteString("extensionType", extension.ExtensionType);
                attrs.WriteString("text", extension.Text);
                attrs.WriteString("layout", extension.Layout);
                attrs.WriteElement("parameters", extension.Parameters);
                break;

            case AdfBodiedExtension bodiedExtension:
                attrs.WriteString("extensionKey", bodiedExtension.ExtensionKey);
                attrs.WriteString("extensionType", bodiedExtension.ExtensionType);
                attrs.WriteString("text", bodiedExtension.Text);
                attrs.WriteString("layout", bodiedExtension.Layout);
                attrs.WriteElement("parameters", bodiedExtension.Parameters);
                break;

            case AdfInlineExtension inlineExtension:
                attrs.WriteString("extensionKey", inlineExtension.ExtensionKey);
                attrs.WriteString("extensionType", inlineExtension.ExtensionType);
                attrs.WriteString("text", inlineExtension.Text);
                attrs.WriteElement("parameters", inlineExtension.Parameters);
                break;

            case AdfEmoji emoji:
                attrs.WriteString("shortName", emoji.ShortName);
                attrs.WriteString("id", emoji.Id);
                attrs.WriteString("text", emoji.Text);
                attrs.WriteString("fallback", emoji.Fallback);
                break;

            case AdfMention mention:
                attrs.WriteString("id", mention.Id);
                attrs.WriteString("text", mention.Text);
                attrs.WriteString("accessLevel", mention.AccessLevel);
                attrs.WriteString("userType", mention.UserType);
                break;

            case AdfDate date:
                attrs.WriteString("timestamp", date.Timestamp);
                break;

            case AdfStatus status:
                attrs.WriteString("text", status.Text);
                attrs.WriteString("color", status.Color);
                attrs.WriteString("style", status.Style);
                break;

            case AdfPlaceholder placeholder:
                attrs.WriteString("text", placeholder.Text);
                break;
        }

        attrs.WriteString("localId", node.LocalId);
        attrs.End();
    }

    internal static string GetTypeName(AdfNode node) => node switch
    {
        AdfUnknownNode unknown => unknown.TypeName,
        _ => node.Kind switch
        {
            AdfNodeKind.Paragraph => "paragraph",
            AdfNodeKind.Heading => "heading",
            AdfNodeKind.Blockquote => "blockquote",
            AdfNodeKind.CodeBlock => "codeBlock",
            AdfNodeKind.Rule => "rule",
            AdfNodeKind.BulletList => "bulletList",
            AdfNodeKind.OrderedList => "orderedList",
            AdfNodeKind.ListItem => "listItem",
            AdfNodeKind.Panel => "panel",
            AdfNodeKind.Table => "table",
            AdfNodeKind.TableRow => "tableRow",
            AdfNodeKind.TableCell => "tableCell",
            AdfNodeKind.TableHeader => "tableHeader",
            AdfNodeKind.Expand => "expand",
            AdfNodeKind.NestedExpand => "nestedExpand",
            AdfNodeKind.TaskList => "taskList",
            AdfNodeKind.TaskItem => "taskItem",
            AdfNodeKind.DecisionList => "decisionList",
            AdfNodeKind.DecisionItem => "decisionItem",
            AdfNodeKind.MediaGroup => "mediaGroup",
            AdfNodeKind.MediaSingle => "mediaSingle",
            AdfNodeKind.Media => "media",
            AdfNodeKind.MediaInline => "mediaInline",
            AdfNodeKind.Caption => "caption",
            AdfNodeKind.LayoutSection => "layoutSection",
            AdfNodeKind.LayoutColumn => "layoutColumn",
            AdfNodeKind.BlockCard => "blockCard",
            AdfNodeKind.EmbedCard => "embedCard",
            AdfNodeKind.InlineCard => "inlineCard",
            AdfNodeKind.Extension => "extension",
            AdfNodeKind.BodiedExtension => "bodiedExtension",
            AdfNodeKind.InlineExtension => "inlineExtension",
            AdfNodeKind.Text => "text",
            AdfNodeKind.HardBreak => "hardBreak",
            AdfNodeKind.Emoji => "emoji",
            AdfNodeKind.Mention => "mention",
            AdfNodeKind.Date => "date",
            AdfNodeKind.Status => "status",
            AdfNodeKind.Placeholder => "placeholder",
            _ => "",
        },
    };

    private static string GetTypeName(AdfMark mark) => mark switch
    {
        AdfUnknownMark unknown => unknown.TypeName,
        _ => mark.Kind switch
        {
            AdfMarkKind.Link => "link",
            AdfMarkKind.Emphasis => "em",
            AdfMarkKind.Strong => "strong",
            AdfMarkKind.Strike => "strike",
            AdfMarkKind.SubSup => "subsup",
            AdfMarkKind.Underline => "underline",
            AdfMarkKind.TextColor => "textColor",
            AdfMarkKind.Annotation => "annotation",
            AdfMarkKind.BackgroundColor => "backgroundColor",
            AdfMarkKind.Code => "code",
            AdfMarkKind.Alignment => "alignment",
            AdfMarkKind.Indentation => "indentation",
            AdfMarkKind.Border => "border",
            AdfMarkKind.Breakout => "breakout",
            _ => "",
        },
    };

    // The attribute is required by the schema, but a document that did not carry one must not gain
    // one when it is written back.
    private static string? GetPanelTypeName(AdfPanelType type) => type switch
    {
        AdfPanelType.Info => "info",
        AdfPanelType.Note => "note",
        AdfPanelType.Tip => "tip",
        AdfPanelType.Warning => "warning",
        AdfPanelType.Error => "error",
        AdfPanelType.Success => "success",
        AdfPanelType.Custom => "custom",
        _ => null,
    };

    private static string? GetMediaTypeName(AdfMediaType type) => type switch
    {
        AdfMediaType.File => "file",
        AdfMediaType.Link => "link",
        AdfMediaType.External => "external",
        AdfMediaType.Image => "image",
        _ => null,
    };

    /// <summary>
    /// Writes an <c>attrs</c> object lazily, so nodes whose attributes are all unset do not emit an
    /// empty object.
    /// </summary>
    private ref struct AttributeScope(Utf8JsonWriter writer)
    {
        private readonly Utf8JsonWriter _writer = writer;
        private bool _started;

        private void EnsureStarted()
        {
            if (!_started)
            {
                _started = true;
                _writer.WriteStartObject("attrs");
            }
        }

        public void WriteString(string name, string? value)
        {
            if (value is null)
                return;

            EnsureStarted();
            _writer.WriteString(name, value);
        }

        public void WriteNumber(string name, int? value)
        {
            if (value is null)
                return;

            EnsureStarted();
            _writer.WriteNumber(name, value.Value);
        }

        public void WriteNumber(string name, double? value)
        {
            if (value is null)
                return;

            EnsureStarted();
            _writer.WriteNumber(name, value.Value);
        }

        public void WriteBoolean(string name, bool? value)
        {
            if (value is null)
                return;

            EnsureStarted();
            _writer.WriteBoolean(name, value.Value);
        }

        public void WriteElement(string name, JsonElement? value)
        {
            if (value is null)
                return;

            EnsureStarted();
            _writer.WritePropertyName(name);
            value.Value.WriteTo(_writer);
        }

        /// <summary>Closes the <c>attrs</c> object when one was started.</summary>
        public void End()
        {
            if (_started)
            {
                _started = false;
                _writer.WriteEndObject();
            }
        }
    }
}
