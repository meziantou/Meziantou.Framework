namespace Meziantou.Framework.AtlassianDataFormat.Tests;

public sealed class AdfToMarkdownTests
{
    private static void AssertMarkdown(string json, string expected, AdfToMarkdownOptions? options = null)
    {
        var actual = options is null
            ? AdfToMarkdown.Convert(json)
            : AdfToMarkdown.Convert(json, options);
        Assert.Equal(expected, actual);
    }

    /// <summary>Wraps top-level nodes in a document, so tests only carry the interesting JSON.</summary>
    private static string Doc(string content) => $$"""{"version":1,"type":"doc","content":[{{content}}]}""";

    private static string Paragraph(string content) => $$"""{"type":"paragraph","content":[{{content}}]}""";

    private static string Text(string text, string? marks = null)
        => marks is null
            ? $$"""{"type":"text","text":"{{text}}"}"""
            : $$"""{"type":"text","text":"{{text}}","marks":[{{marks}}]}""";

    // --- Document ---

    [Fact]
    public void EmptyDocument()
    {
        AssertMarkdown("""{"version":1,"type":"doc","content":[]}""", "");
    }

    [Fact]
    public void PlainParagraph()
    {
        AssertMarkdown(Doc(Paragraph(Text("Hello world"))), "Hello world");
    }

    [Fact]
    public void EmptyParagraphIsDropped()
    {
        AssertMarkdown(
            Doc($$"""{{Paragraph(Text("a"))}},{"type":"paragraph"},{{Paragraph(Text("b"))}}"""),
            "a\n\nb");
    }

    [Fact]
    public void TwoParagraphsSeparatedByBlankLine()
    {
        AssertMarkdown(Doc($"{Paragraph(Text("one"))},{Paragraph(Text("two"))}"), "one\n\ntwo");
    }

    // --- Marks ---

    [Fact]
    public void Strong()
    {
        AssertMarkdown(Doc(Paragraph(Text("Hello", """{"type":"strong"}"""))), "**Hello**");
    }

    [Fact]
    public void Emphasis()
    {
        AssertMarkdown(Doc(Paragraph(Text("Hello", """{"type":"em"}"""))), "*Hello*");
    }

    [Fact]
    public void EmphasisWithUnderscoreMarker()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Hello", """{"type":"em"}"""))),
            "_Hello_",
            new AdfToMarkdownOptions { EmphasisMarker = AdfEmphasisMarker.Underscore });
    }

    [Fact]
    public void Strike()
    {
        AssertMarkdown(Doc(Paragraph(Text("Hello", """{"type":"strike"}"""))), "~~Hello~~");
    }

    [Fact]
    public void InlineCode()
    {
        AssertMarkdown(Doc(Paragraph(Text("var x = 1;", """{"type":"code"}"""))), "`var x = 1;`");
    }

    [Fact]
    public void InlineCodeIsNotEscaped()
    {
        AssertMarkdown(Doc(Paragraph(Text("a*b", """{"type":"code"}"""))), "`a*b`");
    }

    [Fact]
    public void InlineCodeContainingBacktickUsesLongerFence()
    {
        AssertMarkdown(Doc(Paragraph(Text("a`b", """{"type":"code"}"""))), "``a`b``");
    }

    [Fact]
    public void Link()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Example", """{"type":"link","attrs":{"href":"https://example.com"}}"""))),
            "[Example](https://example.com)");
    }

    [Fact]
    public void LinkWithTitle()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Example", """{"type":"link","attrs":{"href":"https://example.com","title":"Home"}}"""))),
            """[Example](https://example.com "Home")""");
    }

    [Fact]
    public void LinkWithSpaceInHrefIsWrappedInAngleBrackets()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Example", """{"type":"link","attrs":{"href":"https://example.com/a b"}}"""))),
            "[Example](<https://example.com/a b>)");
    }

    [Fact]
    public void StrongAndEmphasisAreNestedInSchemaOrder()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Hello", """{"type":"strong"},{"type":"em"}"""))),
            "***Hello***");
    }

    [Fact]
    public void MarkOrderInTheDocumentDoesNotChangeTheOutput()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Hello", """{"type":"em"},{"type":"strong"}"""))),
            "***Hello***");
    }

    [Fact]
    public void CodeCombinedWithLinkPutsTheLinkOutside()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("code", """{"type":"code"},{"type":"link","attrs":{"href":"https://example.com"}}"""))),
            "[`code`](https://example.com)");
    }

    [Fact]
    public void AdjacentTextNodesWithTheSameMarksAreMerged()
    {
        AssertMarkdown(
            Doc(Paragraph($"""{Text("Hello ", """{"type":"strong"}""")},{Text("world", """{"type":"strong"}""")}""")),
            "**Hello world**");
    }

    [Fact]
    public void TrailingSpaceIsMovedOutsideTheEmphasis()
    {
        AssertMarkdown(
            Doc(Paragraph($"""{Text("Hello ", """{"type":"strong"}""")},{Text("world")}""")),
            "**Hello** world");
    }

    [Fact]
    public void SubscriptAndSuperscript()
    {
        AssertMarkdown(
            Doc(Paragraph($"""{Text("a", """{"type":"subsup","attrs":{"type":"sub"}}""")},{Text("b", """{"type":"subsup","attrs":{"type":"sup"}}""")}""")),
            "<sub>a</sub><sup>b</sup>");
    }

    [Fact]
    public void UnderlineAndColorsAreDropped()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Hello", """{"type":"underline"},{"type":"textColor","attrs":{"color":"#ff0000"}}"""))),
            "Hello");
    }

    // --- Escaping ---

    [Fact]
    public void MarkdownSpecialCharactersAreEscaped()
    {
        AssertMarkdown(Doc(Paragraph(Text("a*b[c]d"))), @"a\*b\[c\]d");
    }

    [Fact]
    public void UnderscoreInsideAWordIsNotEscaped()
    {
        AssertMarkdown(Doc(Paragraph(Text("snake_case"))), "snake_case");
    }

    [Fact]
    public void UnderscoreAtAWordBoundaryIsEscaped()
    {
        AssertMarkdown(Doc(Paragraph(Text("_hello_"))), @"\_hello\_");
    }

    [Fact]
    public void LeadingHashIsEscaped()
    {
        AssertMarkdown(Doc(Paragraph(Text("# not a heading"))), @"\# not a heading");
    }

    // --- Headings ---

    [Theory]
    [InlineData(1, "# Title")]
    [InlineData(2, "## Title")]
    [InlineData(6, "###### Title")]
    public void Heading(int level, string expected)
    {
        AssertMarkdown(
            Doc($$"""{"type":"heading","attrs":{"level":{{level}}},"content":[{{Text("Title")}}]}"""),
            expected);
    }

    [Fact]
    public void SetextHeading()
    {
        AssertMarkdown(
            Doc($$"""{"type":"heading","attrs":{"level":1},"content":[{{Text("Title")}}]}"""),
            "Title\n=====",
            new AdfToMarkdownOptions { HeadingStyle = AdfHeadingStyle.Setext });
    }

    [Fact]
    public void SetextHeadingFallsBackToAtxBeyondLevelTwo()
    {
        AssertMarkdown(
            Doc($$"""{"type":"heading","attrs":{"level":3},"content":[{{Text("Title")}}]}"""),
            "### Title",
            new AdfToMarkdownOptions { HeadingStyle = AdfHeadingStyle.Setext });
    }

    // --- Blocks ---

    [Fact]
    public void Rule()
    {
        AssertMarkdown(Doc($$"""{{Paragraph(Text("a"))}},{"type":"rule"},{{Paragraph(Text("b"))}}"""), "a\n\n---\n\nb");
    }

    [Fact]
    public void Blockquote()
    {
        AssertMarkdown(
            Doc($$"""{"type":"blockquote","content":[{{Paragraph(Text("quoted"))}}]}"""),
            "> quoted");
    }

    [Fact]
    public void BlockquoteWithTwoParagraphs()
    {
        AssertMarkdown(
            Doc($$"""{"type":"blockquote","content":[{{Paragraph(Text("one"))}},{{Paragraph(Text("two"))}}]}"""),
            "> one\n>\n> two");
    }

    [Fact]
    public void CodeBlockWithLanguage()
    {
        AssertMarkdown(
            Doc($$"""{"type":"codeBlock","attrs":{"language":"csharp"},"content":[{{Text("var x = 1;")}}]}"""),
            "```csharp\nvar x = 1;\n```");
    }

    [Fact]
    public void CodeBlockWithoutLanguage()
    {
        AssertMarkdown(
            Doc($$"""{"type":"codeBlock","content":[{{Text("plain")}}]}"""),
            "```\nplain\n```");
    }

    [Fact]
    public void CodeBlockContainingAFenceUsesALongerFence()
    {
        AssertMarkdown(
            Doc($$"""{"type":"codeBlock","content":[{{Text("a\\n```\\nb")}}]}"""),
            "````\na\n```\nb\n````");
    }

    [Fact]
    public void IndentedCodeBlock()
    {
        AssertMarkdown(
            Doc($$"""{"type":"codeBlock","content":[{{Text("line1\\nline2")}}]}"""),
            "    line1\n    line2",
            new AdfToMarkdownOptions { CodeBlockStyle = AdfCodeBlockStyle.Indented });
    }

    [Fact]
    public void HardBreak()
    {
        AssertMarkdown(
            Doc(Paragraph($$"""{{Text("line1")}},{"type":"hardBreak"},{{Text("line2")}}""")),
            "line1  \nline2");
    }

    [Fact]
    public void HardBreakWithBackslashStyle()
    {
        AssertMarkdown(
            Doc(Paragraph($$"""{{Text("line1")}},{"type":"hardBreak"},{{Text("line2")}}""")),
            "line1\\\nline2",
            new AdfToMarkdownOptions { LineBreakStyle = AdfLineBreakStyle.Backslash });
    }

    // --- Lists ---

    private static string ListItem(string content) => $$"""{"type":"listItem","content":[{{content}}]}""";

    [Fact]
    public void BulletList()
    {
        AssertMarkdown(
            Doc($$"""{"type":"bulletList","content":[{{ListItem(Paragraph(Text("one")))}},{{ListItem(Paragraph(Text("two")))}}]}"""),
            "- one\n- two");
    }

    [Fact]
    public void BulletListWithCustomMarker()
    {
        AssertMarkdown(
            Doc($$"""{"type":"bulletList","content":[{{ListItem(Paragraph(Text("one")))}}]}"""),
            "* one",
            new AdfToMarkdownOptions { UnorderedListMarker = '*' });
    }

    [Fact]
    public void OrderedList()
    {
        AssertMarkdown(
            Doc($$"""{"type":"orderedList","content":[{{ListItem(Paragraph(Text("one")))}},{{ListItem(Paragraph(Text("two")))}}]}"""),
            "1. one\n2. two");
    }

    [Fact]
    public void OrderedListWithStartNumber()
    {
        AssertMarkdown(
            Doc($$"""{"type":"orderedList","attrs":{"order":3},"content":[{{ListItem(Paragraph(Text("one")))}},{{ListItem(Paragraph(Text("two")))}}]}"""),
            "3. one\n4. two");
    }

    [Fact]
    public void OrderedListStartingAtZero()
    {
        AssertMarkdown(
            Doc($$"""{"type":"orderedList","attrs":{"order":0},"content":[{{ListItem(Paragraph(Text("one")))}}]}"""),
            "0. one");
    }

    [Fact]
    public void NestedBulletList()
    {
        var nested = $$"""{"type":"bulletList","content":[{{ListItem(Paragraph(Text("sub")))}}]}""";
        AssertMarkdown(
            Doc($$"""{"type":"bulletList","content":[{"type":"listItem","content":[{{Paragraph(Text("one"))}},{{nested}}]}]}"""),
            "- one\n\n  - sub");
    }

    [Fact]
    public void CodeBlockInsideAListItemIsIndented()
    {
        AssertMarkdown(
            Doc($$"""{"type":"bulletList","content":[{"type":"listItem","content":[{{Paragraph(Text("one"))}},{"type":"codeBlock","content":[{{Text("code")}}]}]}]}"""),
            "- one\n\n  ```\n  code\n  ```");
    }

    // --- Panels ---

    [Fact]
    public void InfoPanel()
    {
        AssertMarkdown(
            Doc($$"""{"type":"panel","attrs":{"panelType":"info"},"content":[{{Paragraph(Text("Careful"))}}]}"""),
            "> ℹ️ Careful");
    }

    [Fact]
    public void PanelAsGitHubAlert()
    {
        AssertMarkdown(
            Doc($$"""{"type":"panel","attrs":{"panelType":"warning"},"content":[{{Paragraph(Text("Careful"))}}]}"""),
            "> [!WARNING]\n> Careful",
            new AdfToMarkdownOptions { PanelStyle = AdfPanelStyle.GitHubAlert });
    }

    [Fact]
    public void PanelAsPlainBlockquote()
    {
        AssertMarkdown(
            Doc($$"""{"type":"panel","attrs":{"panelType":"info"},"content":[{{Paragraph(Text("Careful"))}}]}"""),
            "> Careful",
            new AdfToMarkdownOptions { PanelStyle = AdfPanelStyle.PlainText });
    }

    [Fact]
    public void PanelStartingWithAListKeepsTheMarkerOnItsOwnLine()
    {
        AssertMarkdown(
            Doc($$"""{"type":"panel","attrs":{"panelType":"info"},"content":[{"type":"bulletList","content":[{{ListItem(Paragraph(Text("one")))}}]}]}"""),
            "> ℹ️\n>\n> - one");
    }

    // --- Expand ---

    [Fact]
    public void Expand()
    {
        AssertMarkdown(
            Doc($$"""{"type":"expand","attrs":{"title":"More"},"content":[{{Paragraph(Text("Body"))}}]}"""),
            "> **More**\n>\n> Body");
    }

    [Fact]
    public void ExpandAsHtmlDetails()
    {
        AssertMarkdown(
            Doc($$"""{"type":"expand","attrs":{"title":"More"},"content":[{{Paragraph(Text("Body"))}}]}"""),
            "<details>\n<summary>More</summary>\n\nBody\n\n</details>",
            new AdfToMarkdownOptions { ExpandStyle = AdfExpandStyle.HtmlDetails });
    }

    // --- Tasks and decisions ---

    [Fact]
    public void TaskList()
    {
        AssertMarkdown(
            Doc($$"""{"type":"taskList","attrs":{"localId":"l"},"content":[{"type":"taskItem","attrs":{"localId":"a","state":"TODO"},"content":[{{Text("todo")}}]},{"type":"taskItem","attrs":{"localId":"b","state":"DONE"},"content":[{{Text("done")}}]}]}"""),
            "- [ ] todo\n- [x] done");
    }

    [Fact]
    public void TaskListWithoutCheckboxes()
    {
        AssertMarkdown(
            Doc($$"""{"type":"taskList","attrs":{"localId":"l"},"content":[{"type":"taskItem","attrs":{"localId":"a","state":"DONE"},"content":[{{Text("done")}}]}]}"""),
            "- done",
            new AdfToMarkdownOptions { TaskListStyle = AdfTaskListStyle.PlainText });
    }

    [Fact]
    public void DecisionList()
    {
        AssertMarkdown(
            Doc($$"""{"type":"decisionList","attrs":{"localId":"l"},"content":[{"type":"decisionItem","attrs":{"localId":"a","state":"DECIDED"},"content":[{{Text("ship it")}}]}]}"""),
            "- ship it");
    }

    // --- Inline nodes ---

    [Fact]
    public void Mention()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"mention","attrs":{"id":"123","text":"@Alex"}}""")),
            "@Alex");
    }

    [Fact]
    public void MentionWithoutTextFallsBackToTheIdentifier()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"mention","attrs":{"id":"123"}}""")),
            "@123");
    }

    [Fact]
    public void MentionWithCustomFormat()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"mention","attrs":{"id":"123","text":"@Alex"}}""")),
            "Alex (123)",
            new AdfToMarkdownOptions { MentionFormat = "{text} ({id})" });
    }

    [Fact]
    public void MentionWithResolver()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"mention","attrs":{"id":"123"}}""")),
            "@Alex",
            new AdfToMarkdownOptions { MentionResolver = _ => "Alex" });
    }

    [Fact]
    public void Emoji()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"emoji","attrs":{"shortName":":grinning:","text":"😀"}}""")),
            "😀");
    }

    [Fact]
    public void EmojiAsShortName()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"emoji","attrs":{"shortName":":grinning:","text":"😀"}}""")),
            ":grinning:",
            new AdfToMarkdownOptions { EmojiRendering = AdfEmojiRendering.ShortName });
    }

    [Fact]
    public void EmojiWithoutTextFallsBackToTheShortName()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"emoji","attrs":{"shortName":":grinning:"}}""")),
            ":grinning:");
    }

    [Fact]
    public void DateUsesMilliseconds()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"date","attrs":{"timestamp":"1704067200000"}}""")),
            "2024-01-01");
    }

    [Fact]
    public void DateWithCustomFormat()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"date","attrs":{"timestamp":"1704067200000"}}""")),
            "01/01/2024",
            new AdfToMarkdownOptions { DateFormat = "dd/MM/yyyy" });
    }

    [Fact]
    public void Status()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"status","attrs":{"text":"In progress","color":"blue"}}""")),
            "`In progress`");
    }

    [Fact]
    public void StatusWithCustomFormat()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"status","attrs":{"text":"In progress","color":"blue"}}""")),
            "**[In progress]**",
            new AdfToMarkdownOptions { StatusFormat = "**[{text}]**" });
    }

    [Fact]
    public void PlaceholderIsDropped()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("a") + "," + PlaceholderJson)),
            "a");
    }

    [Fact]
    public void InlineCard()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"inlineCard","attrs":{"url":"https://example.com"}}""")),
            "<https://example.com>");
    }

    // --- Media ---

    private const string PlaceholderJson = """{"type":"placeholder","attrs":{"text":"type here"}}""";

    private const string ExternalMediaJson = """{"type":"media","attrs":{"type":"external","url":"https://example.com/a.png","alt":"A picture"}}""";

    private const string InlineMediaJson = """{"type":"mediaInline","attrs":{"type":"file","id":"abc","collection":"c","alt":"file.pdf"}}""";

    private const string StoredMediaJson = """{"type":"media","attrs":{"type":"file","id":"abc","collection":"c","alt":"A picture"}}""";

    private static string MediaSingle(string content)
        => """{"type":"mediaSingle","attrs":{"layout":"center"},"content":[""" + content + "]}";

    [Fact]
    public void ExternalMedia()
    {
        AssertMarkdown(
            Doc(MediaSingle(ExternalMediaJson)),
            "![A picture](https://example.com/a.png)");
    }

    [Fact]
    public void MediaWithCaption()
    {
        AssertMarkdown(
            Doc(MediaSingle(ExternalMediaJson + "," + """{"type":"caption","content":[""" + Text("The caption") + "]}")),
            "![A picture](https://example.com/a.png)\n\n*The caption*");
    }

    [Fact]
    public void StoredMediaWithoutResolverFallsBackToTheAlternativeText()
    {
        AssertMarkdown(
            Doc(MediaSingle(StoredMediaJson)),
            "A picture");
    }

    [Fact]
    public void StoredMediaWithResolver()
    {
        AssertMarkdown(
            Doc(MediaSingle(StoredMediaJson)),
            "![A picture](https://cdn.example.com/abc)",
            new AdfToMarkdownOptions { MediaUrlResolver = media => "https://cdn.example.com/" + media.Id });
    }

    [Fact]
    public void MediaCanBeSkipped()
    {
        AssertMarkdown(
            Doc(MediaSingle(ExternalMediaJson)),
            "",
            new AdfToMarkdownOptions { MediaRendering = AdfMediaRendering.Skip });
    }

    // --- Tables ---

    private static string Cell(string type, string text) => $$"""{"type":"{{type}}","content":[{{Paragraph(Text(text))}}]}""";

    private static string Row(params string[] cells) => $$"""{"type":"tableRow","content":[{{string.Join(",", cells)}}]}""";

    [Fact]
    public void TableWithHeaderRow()
    {
        AssertMarkdown(
            Doc($$"""{"type":"table","content":[{{Row(Cell("tableHeader", "a"), Cell("tableHeader", "b"))}},{{Row(Cell("tableCell", "c"), Cell("tableCell", "d"))}}]}"""),
            "| a | b |\n| --- | --- |\n| c | d |");
    }

    [Fact]
    public void TableWithoutHeaderRowGetsAnEmptyOne()
    {
        AssertMarkdown(
            Doc($$"""{"type":"table","content":[{{Row(Cell("tableCell", "a"), Cell("tableCell", "b"))}}]}"""),
            "|  |  |\n| --- | --- |\n| a | b |");
    }

    [Fact]
    public void TableCellWithBlockContentIsFlattened()
    {
        var cell = $$"""{"type":"tableCell","content":[{{Paragraph(Text("one"))}},{{Paragraph(Text("two"))}}]}""";
        AssertMarkdown(
            Doc($$"""{"type":"table","content":[{{Row(Cell("tableHeader", "h"))}},{{Row(cell)}}]}"""),
            "| h |\n| --- |\n| one<br>two |");
    }

    [Fact]
    public void TableWithSpansUsesHtmlWhenAutomatic()
    {
        var cell = $$"""{"type":"tableCell","attrs":{"colspan":2},"content":[{{Paragraph(Text("wide"))}}]}""";
        AssertMarkdown(
            Doc($$"""{"type":"table","content":[{{Row(cell)}}]}"""),
            "<table>\n<tr>\n<td colspan=\"2\">\n\nwide\n\n</td>\n</tr>\n</table>",
            new AdfToMarkdownOptions { TableStyle = AdfTableStyle.Auto });
    }

    [Fact]
    public void PipeCharacterInACellIsEscaped()
    {
        AssertMarkdown(
            Doc($$"""{"type":"table","content":[{{Row(Cell("tableHeader", "h"))}},{{Row(Cell("tableCell", "a|b"))}}]}"""),
            "| h |\n| --- |\n| a\\|b |");
    }

    // --- Layout ---

    [Fact]
    public void LayoutColumnsAreFlattened()
    {
        var column = $$"""{"type":"layoutColumn","attrs":{"width":50},"content":[{{Paragraph(Text("left"))}}]}""";
        var column2 = $$"""{"type":"layoutColumn","attrs":{"width":50},"content":[{{Paragraph(Text("right"))}}]}""";
        AssertMarkdown(
            Doc($$"""{"type":"layoutSection","content":[{{column}},{{column2}}]}"""),
            "left\n\nright");
    }

    // --- Unknown nodes ---

    [Fact]
    public void UnknownNodeIsSkipped()
    {
        AssertMarkdown(
            Doc($$"""{{Paragraph(Text("a"))}},{"type":"somethingNew","content":[{{Paragraph(Text("inner"))}}]}"""),
            "a");
    }

    [Fact]
    public void UnknownNodeContentCanBeKept()
    {
        AssertMarkdown(
            Doc($$"""{{Paragraph(Text("a"))}},{"type":"somethingNew","content":[{{Paragraph(Text("inner"))}}]}"""),
            "a\n\ninner",
            new AdfToMarkdownOptions { UnknownNodeHandling = AdfUnknownNodeHandling.KeepContent });
    }

    // --- Cards, extensions and remaining containers ---

    [Fact]
    public void BlockCard()
    {
        AssertMarkdown(
            Doc("""{"type":"blockCard","attrs":{"url":"https://example.com"}}"""),
            "<https://example.com>");
    }

    [Fact]
    public void BlockCardWithJsonLdDataUsesTheName()
    {
        AssertMarkdown(
            Doc("""{"type":"blockCard","attrs":{"data":{"name":"My page","url":"https://example.com/p"}}}"""),
            "[My page](https://example.com/p)");
    }

    [Fact]
    public void EmbedCard()
    {
        AssertMarkdown(
            Doc("""{"type":"embedCard","attrs":{"url":"https://example.com","layout":"wide"}}"""),
            "<https://example.com>");
    }

    [Fact]
    public void ExtensionFallsBackToItsText()
    {
        AssertMarkdown(
            Doc("""{"type":"extension","attrs":{"extensionKey":"drawio","extensionType":"com.example","text":"Diagram"}}"""),
            "Diagram");
    }

    [Fact]
    public void BodiedExtensionConvertsItsContent()
    {
        AssertMarkdown(
            Doc($$"""{"type":"bodiedExtension","attrs":{"extensionKey":"k","extensionType":"t"},"content":[{{Paragraph(Text("inside"))}}]}"""),
            "inside");
    }

    [Fact]
    public void InlineExtensionFallsBackToItsText()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"inlineExtension","attrs":{"extensionKey":"k","extensionType":"t","text":"macro"}}""")),
            "macro");
    }

    [Fact]
    public void NestedExpand()
    {
        var inner = $$"""{"type":"nestedExpand","attrs":{"title":"Inner"},"content":[{{Paragraph(Text("Body"))}}]}""";
        AssertMarkdown(
            Doc($$"""{"type":"expand","attrs":{"title":"Outer"},"content":[{{inner}}]}"""),
            "> **Outer**\n>\n> > **Inner**\n> >\n> > Body");
    }

    [Fact]
    public void ExpandAsHeading()
    {
        AssertMarkdown(
            Doc($$"""{"type":"expand","attrs":{"title":"More"},"content":[{{Paragraph(Text("Body"))}}]}"""),
            "### More\n\nBody",
            new AdfToMarkdownOptions { ExpandStyle = AdfExpandStyle.Heading });
    }

    [Fact]
    public void MediaGroup()
    {
        AssertMarkdown(
            Doc("""{"type":"mediaGroup","content":[{"type":"media","attrs":{"type":"external","url":"https://example.com/a.png","alt":"A"}},{"type":"media","attrs":{"type":"external","url":"https://example.com/b.png","alt":"B"}}]}"""),
            "![A](https://example.com/a.png)\n\n![B](https://example.com/b.png)");
    }

    [Fact]
    public void InlineMedia()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("Before ") + "," + InlineMediaJson + "," + Text(" after"))),
            "Before file.pdf after");
    }

    [Fact]
    public void MediaWrappedInALinkMark()
    {
        const string Media = """{"type":"media","attrs":{"type":"external","url":"https://example.com/a.png","alt":"A"},"marks":[{"type":"link","attrs":{"href":"https://example.com"}}]}""";
        AssertMarkdown(
            Doc(MediaSingle(Media)),
            "[![A](https://example.com/a.png)](https://example.com)");
    }

    [Fact]
    public void MediaAsALink()
    {
        AssertMarkdown(
            Doc(MediaSingle(ExternalMediaJson)),
            "[A picture](https://example.com/a.png)",
            new AdfToMarkdownOptions { MediaRendering = AdfMediaRendering.Link });
    }

    [Fact]
    public void MediaAsAlternativeTextOnly()
    {
        AssertMarkdown(
            Doc(MediaSingle(ExternalMediaJson)),
            "A picture",
            new AdfToMarkdownOptions { MediaRendering = AdfMediaRendering.AltText });
    }

    // --- Remaining options ---

    [Fact]
    public void PanelAsHtml()
    {
        AssertMarkdown(
            Doc($$"""{"type":"panel","attrs":{"panelType":"note"},"content":[{{Paragraph(Text("content"))}}]}"""),
            "<div data-panel-type=\"note\">\n\ncontent\n\n</div>",
            new AdfToMarkdownOptions { PanelStyle = AdfPanelStyle.Html });
    }

    [Fact]
    public void DecisionListAsPlainText()
    {
        AssertMarkdown(
            Doc($$"""{"type":"decisionList","attrs":{"localId":"l"},"content":[{"type":"decisionItem","attrs":{"localId":"a","state":"DECIDED"},"content":[{{Text("one")}}]},{"type":"decisionItem","attrs":{"localId":"b","state":"DECIDED"},"content":[{{Text("two")}}]}]}"""),
            "one\n\ntwo",
            new AdfToMarkdownOptions { DecisionListStyle = AdfDecisionListStyle.PlainText });
    }

    [Fact]
    public void TableAsHtml()
    {
        AssertMarkdown(
            Doc($$"""{"type":"table","content":[{{Row(Cell("tableHeader", "h"))}},{{Row(Cell("tableCell", "c"))}}]}"""),
            "<table>\n<tr>\n<th>\n\nh\n\n</th>\n</tr>\n<tr>\n<td>\n\nc\n\n</td>\n</tr>\n</table>",
            new AdfToMarkdownOptions { TableStyle = AdfTableStyle.Html });
    }

    [Fact]
    public void TableStaysAPipeTableWhenAutomaticAndSimple()
    {
        AssertMarkdown(
            Doc($$"""{"type":"table","content":[{{Row(Cell("tableHeader", "a"))}},{{Row(Cell("tableCell", "b"))}}]}"""),
            "| a |\n| --- |\n| b |",
            new AdfToMarkdownOptions { TableStyle = AdfTableStyle.Auto });
    }

    [Fact]
    public void CustomThematicBreakAndCodeFence()
    {
        AssertMarkdown(
            Doc($$"""{"type":"rule"},{"type":"codeBlock","content":[{{Text("x")}}]}"""),
            "***\n\n~~~\nx\n~~~",
            new AdfToMarkdownOptions { ThematicBreak = "***", CodeBlockFenceCharacter = '~' });
    }

    // --- Cases found by running the converter over the third-party corpora ---

    [Fact]
    public void CodeBlockContentEndingWithANewlineDoesNotEmitABlankLine()
    {
        AssertMarkdown(
            Doc($$"""{"type":"codeBlock","attrs":{"language":"python"},"content":[{{Text("print('hello')\\n")}}]}"""),
            "```python\nprint('hello')\n```");
    }

    [Fact]
    public void BackslashInALinkTitleIsEscaped()
    {
        AssertMarkdown(
            Doc(Paragraph(Text("x", """{"type":"link","attrs":{"href":"https://example.com","title":"a \\\\ b"}}"""))),
            """[x](https://example.com "a \\\\ b")""");
    }

    [Fact]
    public void EmojiFallbackAttributeIsUsedWhenTextIsMissing()
    {
        AssertMarkdown(
            Doc(Paragraph("""{"type":"emoji","attrs":{"shortName":":smile:","fallback":"😄"}}""")),
            "😄");
    }

    [Fact]
    public void EmojiWithOnlyAFallbackAndNoShortName()
    {
        AssertMarkdown(Doc(Paragraph("""{"type":"emoji","attrs":{"fallback":"😊"}}""")), "😊");
    }

    [Fact]
    public void TaskItemWrappingItsContentInAParagraph()
    {
        AssertMarkdown(
            Doc($$"""{"type":"taskList","attrs":{"localId":"l"},"content":[{"type":"taskItem","attrs":{"localId":"a","state":"TODO"},"content":[{{Paragraph(Text("todo"))}}]}]}"""),
            "- [ ] todo");
    }

    [Fact]
    public void DecisionItemWithSeveralParagraphs()
    {
        AssertMarkdown(
            Doc($$"""{"type":"decisionList","attrs":{"localId":"l"},"content":[{"type":"decisionItem","attrs":{"localId":"a","state":"DECIDED"},"content":[{{Paragraph(Text("First"))}},{{Paragraph(Text("Second"))}}]}]}"""),
            "- First\n\n  Second");
    }

    [Fact]
    public void UnknownNodeCanThrow()
    {
        var json = Doc("""{"type":"somethingNew"}""");
        var options = new AdfToMarkdownOptions { UnknownNodeHandling = AdfUnknownNodeHandling.Throw };
        Assert.Throws<AdfException>(() => AdfToMarkdown.Convert(json, options));
    }
}
