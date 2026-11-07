using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.AtlassianDataFormat.Fuzz;

/// <summary>
/// An invalid document must only throw <see cref="AdfException"/>. The overloads of the parser must agree, the JSON a
/// document is written to must parse to the same document, and the Markdown of a document must not depend on whether
/// it was read from its JSON or from the JSON it was written to.
/// </summary>
public static class AtlassianDataFormatFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\", "\\u0000", "\\ud800", "null", "true", "false", "0", "-1", "1e400",
        "\"type\"", "\"version\"", "\"content\"", "\"text\"", "\"marks\"", "\"attrs\"", "\"doc\"", "\"paragraph\"", "\"heading\"", "\"level\"",
        "\"bulletList\"", "\"orderedList\"", "\"listItem\"", "\"order\"", "\"codeBlock\"", "\"language\"", "\"blockquote\"", "\"panel\"", "\"panelType\"",
        "\"table\"", "\"tableRow\"", "\"tableCell\"", "\"tableHeader\"", "\"colspan\"", "\"rowspan\"", "\"mediaSingle\"", "\"mediaGroup\"", "\"mediaInline\"", "\"media\"",
        "\"mention\"", "\"emoji\"", "\"shortName\"", "\"status\"", "\"color\"", "\"date\"", "\"timestamp\"", "\"inlineCard\"", "\"blockCard\"", "\"embedCard\"", "\"url\"",
        "\"rule\"", "\"hardBreak\"", "\"taskList\"", "\"taskItem\"", "\"state\"", "\"DONE\"", "\"decisionList\"", "\"decisionItem\"", "\"expand\"", "\"nestedExpand\"", "\"title\"",
        "\"layoutSection\"", "\"layoutColumn\"", "\"width\"", "\"extension\"", "\"bodiedExtension\"", "\"inlineExtension\"", "\"placeholder\"", "\"caption\"",
        "\"strong\"", "\"em\"", "\"code\"", "\"link\"", "\"href\"", "\"strike\"", "\"underline\"", "\"textColor\"", "\"subsup\"", "\"backgroundColor\"", "\"alignment\"", "\"indentation\"", "\"breakout\"", "\"border\"", "\"annotation\"",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("adf-document", Document) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>Parses a document (after 4 option bytes, the options of the Markdown), writes it, parses it again, and converts it to Markdown.</summary>
    private static void Document(ReadOnlySpan<byte> data)
    {
        var options = ReadOptions(ref data);
        var json = FuzzText.Decode(data);

        AdfDocument document;
        try
        {
            document = AdfDocument.Parse(json);
        }
        catch (AdfException)
        {
            FuzzAssert.False(AdfDocument.TryParse(json, out _), "TryParse accepts a document that Parse rejects");
            return;
        }

        FuzzAssert.True(AdfDocument.TryParse(json, out var tried), "TryParse rejects a document that Parse accepts");
        var written = document.ToJsonString();
        FuzzAssert.Equal(written, tried.ToJsonString(), "TryParse and Parse give different documents");
        _ = document.Descendants().Count();

        using (var element = JsonDocument.Parse(json))
        {
            FuzzAssert.Equal(written, AdfDocument.Parse(element.RootElement).ToJsonString(), "The JsonElement overload gives another document");
        }

        if (TryParseNode(json) is { } node)
        {
            // Writing the node reads every string, also those the reader ignores: an escaped unpaired surrogate there
            // only fails this overload
            try
            {
                FuzzAssert.Equal(written, AdfDocument.Parse(node).ToJsonString(), "The JsonNode overload gives another document");
            }
            catch (AdfException ex) when (ex.InnerException is InvalidOperationException)
            {
            }
        }

        AdfDocument reparsed;
        try
        {
            reparsed = AdfDocument.Parse(written);
            FuzzAssert.Equal(written, AdfDocument.Parse(document.ToJsonString(indented: true)).ToJsonString(), "The indented JSON of the document parses to another document");
        }
        catch (AdfException ex)
        {
            FuzzAssert.Fail($"The JSON of the document is not a valid document: {ex.Message}");
            return;
        }

        FuzzAssert.Equal(written, reparsed.ToJsonString(), "The JSON of the document parses to another document");

        string markdown;
        try
        {
            markdown = document.ToMarkdown(options);
        }
        catch (AdfException) when (options.UnknownNodeHandling == AdfUnknownNodeHandling.Throw)
        {
            return;
        }

        FuzzAssert.Equal(markdown, reparsed.ToMarkdown(options), "The document read from its JSON converts to other Markdown");
    }

    private static JsonNode? TryParseNode(string json)
    {
        // JsonNode rejects what JsonDocument accepts, such as duplicate property names
        try
        {
            return JsonNode.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static AdfToMarkdownOptions ReadOptions(ref ReadOnlySpan<byte> data)
    {
        var b0 = FuzzInput.ReadByte(ref data);
        var b1 = FuzzInput.ReadByte(ref data);
        var b2 = FuzzInput.ReadByte(ref data);
        var b3 = FuzzInput.ReadByte(ref data);
        return new AdfToMarkdownOptions
        {
            HeadingStyle = Pick<AdfHeadingStyle>(b0),
            EmphasisMarker = Pick<AdfEmphasisMarker>(b0 >> 2),
            CodeBlockStyle = Pick<AdfCodeBlockStyle>(b0 >> 4),
            CodeBlockFenceCharacter = (b0 & 0x40) != 0 ? '~' : '`',
            UnorderedListMarker = FuzzInput.Pick(['-', '*', '+'], b1),
            ThematicBreak = (b1 & 0x04) != 0 ? "***" : "---",
            LineBreakStyle = Pick<AdfLineBreakStyle>(b1 >> 3),
            PanelStyle = Pick<AdfPanelStyle>(b1 >> 5),
            ExpandStyle = Pick<AdfExpandStyle>(b2),
            TableStyle = Pick<AdfTableStyle>(b2 >> 2),
            MediaRendering = Pick<AdfMediaRendering>(b2 >> 4),
            EmojiRendering = Pick<AdfEmojiRendering>(b2 >> 6),
            TaskListStyle = Pick<AdfTaskListStyle>(b3),
            DecisionListStyle = Pick<AdfDecisionListStyle>(b3 >> 2),
            UnknownNodeHandling = Pick<AdfUnknownNodeHandling>(b3 >> 4),
            MentionFormat = (b3 & 0x40) != 0 ? "[{text}]({id})" : "@{text}",
            DateFormat = (b3 & 0x80) != 0 ? "O" : "yyyy-MM-dd",
        };

        static T Pick<T>(int value)
            where T : struct, Enum => FuzzInput.Pick(Enum.GetValues<T>(), (byte)value);
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        var assembly = typeof(AtlassianDataFormatFuzzTargets).Assembly;
        const string Prefix = "Seeds/adf/";

        // The names of the resources use the directory separator of the OS that built the project (%(RecursiveDir))
        var index = 0;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.Replace('\\', '/').StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var text = FuzzResources.ReadText(assembly, name);
            byte[] prefix = [(byte)(index * 37), (byte)(index * 101), (byte)(index * 13), (byte)(index * 7)];
            index++;
            yield return FuzzSeed.FromText(name.Replace('\\', '/')[Prefix.Length..], prefix, text);
        }
    }
}
