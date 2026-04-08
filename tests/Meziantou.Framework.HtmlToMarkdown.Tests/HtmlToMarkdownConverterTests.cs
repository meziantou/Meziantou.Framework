namespace Meziantou.Framework.HtmlToMarkdownTests;

public sealed class HtmlToMarkdownConverterTests
{
    private static void AssertHtmlToMarkdown(string html, string expectedMarkdown, HtmlToMarkdownOptions options = null)
    {
        var actual = options is null
            ? HtmlToMarkdown.Convert(html)
            : HtmlToMarkdown.Convert(html, options);
        Assert.Equal(expectedMarkdown, actual);
    }

    // --- Empty / trivial inputs ---

    [Fact]
    public void EmptyString()
    {
        AssertHtmlToMarkdown(
            "",
            "");
    }

    [Fact]
    public void WhitespaceOnly()
    {
        AssertHtmlToMarkdown(
            "   ",
            "");
    }

    [Fact]
    public void PlainText()
    {
        AssertHtmlToMarkdown(
            "plain text",
            "plain text");
    }

    [Fact]
    public void PlainText_MultipleWords()
    {
        AssertHtmlToMarkdown(
            "hello world",
            "hello world");
    }

    // --- Headings (ATX) ---

    [Fact]
    public void Heading_H1()
    {
        AssertHtmlToMarkdown(
            "<h1>Title</h1>",
            "# Title");
    }

    [Fact]
    public void Heading_H2()
    {
        AssertHtmlToMarkdown(
            "<h2>Subtitle</h2>",
            "## Subtitle");
    }

    [Fact]
    public void Heading_H3()
    {
        AssertHtmlToMarkdown(
            "<h3>H3</h3>",
            "### H3");
    }

    [Fact]
    public void Heading_H4()
    {
        AssertHtmlToMarkdown(
            "<h4>H4</h4>",
            "#### H4");
    }

    [Fact]
    public void Heading_H5()
    {
        AssertHtmlToMarkdown(
            "<h5>H5</h5>",
            "##### H5");
    }

    [Fact]
    public void Heading_H6()
    {
        AssertHtmlToMarkdown(
            "<h6>H6</h6>",
            "###### H6");
    }

    [Fact]
    public void Heading_Setext_H1()
    {
        AssertHtmlToMarkdown(
            "<h1>Title</h1>",
            """
            Title
            =====
            """,
            options: new() { HeadingStyle = HeadingStyle.Setext });
    }

    [Fact]
    public void Heading_Setext_H2()
    {
        AssertHtmlToMarkdown(
            "<h2>Subtitle</h2>",
            """
            Subtitle
            --------
            """,
            options: new() { HeadingStyle = HeadingStyle.Setext });
    }

    [Fact]
    public void Heading_Setext_FallsBackToAtxForH3()
    {
        AssertHtmlToMarkdown(
            "<h3>H3</h3>",
            "### H3",
            options: new() { HeadingStyle = HeadingStyle.Setext });
    }

    [Fact]
    public void Heading_Setext_FallsBackToAtxForH4()
    {
        AssertHtmlToMarkdown(
            "<h4>H4</h4>",
            "#### H4",
            options: new() { HeadingStyle = HeadingStyle.Setext });
    }

    [Fact]
    public void Heading_WithInlineFormatting()
    {
        AssertHtmlToMarkdown(
            "<h1><strong>Bold</strong> heading</h1>",
            "# **Bold** heading");
    }

    [Fact]
    public void Heading_WithBackticks()
    {
        AssertHtmlToMarkdown(
            "<h1>Sample `foo`</h1>",
            "# Sample \\`foo\\`");
    }

    [Fact]
    public void Heading_WithSpecialChars()
    {
        AssertHtmlToMarkdown(
            "<h1>Use *asterisks* and _underscores_</h1>",
            "# Use \\*asterisks\\* and \\_underscores\\_");
    }

    [Fact]
    public void Heading_WithBracketsAndParens()
    {
        AssertHtmlToMarkdown(
            "<h1>See [link](url) for details</h1>",
            "# See \\[link\\](url) for details");
    }

    [Fact]
    public void Heading_Empty()
    {
        AssertHtmlToMarkdown(
            "<h1></h1>",
            "");
    }

    // --- Paragraphs ---

    [Fact]
    public void Paragraph_Single()
    {
        AssertHtmlToMarkdown(
            "<p>Hello world</p>",
            "Hello world");
    }

    [Fact]
    public void Paragraph_Multiple()
    {
        AssertHtmlToMarkdown(
            """
            <p>First paragraph</p>
            <p>Second paragraph</p>
            """,
            """
            First paragraph

            Second paragraph
            """);
    }

    [Fact]
    public void Paragraph_WithLineBreak()
    {
        AssertHtmlToMarkdown(
            "<p>Line one<br>Line two</p>",
            "Line one  \nLine two");
    }

    [Fact]
    public void Paragraph_WithLineBreak_Backslash()
    {
        AssertHtmlToMarkdown(
            "<p>Line one<br>Line two</p>",
            "Line one\\\nLine two",
            options: new() { LineBreakStyle = LineBreakStyle.Backslash });
    }

    // --- Strong / Bold ---

    [Fact]
    public void Strong_Tag()
    {
        AssertHtmlToMarkdown(
            "<strong>bold</strong>",
            "**bold**");
    }

    [Fact]
    public void Strong_BTag()
    {
        AssertHtmlToMarkdown(
            "<b>bold</b>",
            "**bold**");
    }

    [Fact]
    public void Strong_Underscore_StrongTag()
    {
        AssertHtmlToMarkdown(
            "<strong>bold</strong>",
            "__bold__",
            options: new() { EmphasisMarker = EmphasisMarker.Underscore });
    }

    [Fact]
    public void Strong_Underscore_BTag()
    {
        AssertHtmlToMarkdown(
            "<b>bold</b>",
            "__bold__",
            options: new() { EmphasisMarker = EmphasisMarker.Underscore });
    }

    [Fact]
    public void Strong_Empty()
    {
        AssertHtmlToMarkdown(
            "<strong></strong>",
            "");
    }

    // --- Emphasis / Italic ---

    [Fact]
    public void Emphasis_EmTag()
    {
        AssertHtmlToMarkdown(
            "<em>italic</em>",
            "*italic*");
    }

    [Fact]
    public void Emphasis_ITag()
    {
        AssertHtmlToMarkdown(
            "<i>italic</i>",
            "*italic*");
    }

    [Fact]
    public void Emphasis_Underscore_EmTag()
    {
        AssertHtmlToMarkdown(
            "<em>italic</em>",
            "_italic_",
            options: new() { EmphasisMarker = EmphasisMarker.Underscore });
    }

    [Fact]
    public void Emphasis_Underscore_ITag()
    {
        AssertHtmlToMarkdown(
            "<i>italic</i>",
            "_italic_",
            options: new() { EmphasisMarker = EmphasisMarker.Underscore });
    }

    // --- Combined emphasis ---

    [Fact]
    public void CombinedEmphasis_StrongOutside()
    {
        AssertHtmlToMarkdown(
            "<strong><em>bold italic</em></strong>",
            "***bold italic***");
    }

    [Fact]
    public void CombinedEmphasis_EmOutside()
    {
        AssertHtmlToMarkdown(
            "<em><strong>bold italic</strong></em>",
            "***bold italic***");
    }

    // --- Strikethrough ---

    [Fact]
    public void Strikethrough_DelTag()
    {
        AssertHtmlToMarkdown(
            "<del>deleted</del>",
            "~~deleted~~");
    }

    [Fact]
    public void Strikethrough_STag()
    {
        AssertHtmlToMarkdown(
            "<s>strikethrough</s>",
            "~~strikethrough~~");
    }

    [Fact]
    public void Strikethrough_StrikeTag()
    {
        AssertHtmlToMarkdown(
            "<strike>strikethrough</strike>",
            "~~strikethrough~~");
    }

    // --- Inline code ---

    [Fact]
    public void InlineCode_Simple()
    {
        AssertHtmlToMarkdown(
            "<code>code</code>",
            "`code`");
    }

    [Fact]
    public void InlineCode_ContainingBacktick()
    {
        AssertHtmlToMarkdown(
            "<code>a `b` c</code>",
            "``a `b` c``");
    }

    [Fact]
    public void InlineCode_BackticksAtStartAndEnd()
    {
        AssertHtmlToMarkdown(
            "<code>`code`</code>",
            "`` `code` ``");
    }

    // --- Links ---

    [Fact]
    public void Link_Simple()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com">text</a>""",
            "[text](https://example.com)");
    }

    [Fact]
    public void Link_WithTitle()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com" title="Title">text</a>""",
            """[text](https://example.com "Title")""");
    }

    [Fact]
    public void Link_NoHref()
    {
        AssertHtmlToMarkdown(
            "<a>text</a>",
            "text");
    }

    [Fact]
    public void Link_EmptyHref()
    {
        AssertHtmlToMarkdown(
            """<a href="">text</a>""",
            "[text]()");
    }

    [Fact]
    public void Link_WithInlineFormatting()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com"><strong>bold link</strong></a>""",
            "[**bold link**](https://example.com)");
    }

    [Fact]
    public void Link_UrlWithParentheses()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com/path_(with_parens)">text</a>""",
            "[text](https://example.com/path_(with_parens))");
    }

    // --- Images ---

    [Fact]
    public void Image_WithAlt()
    {
        AssertHtmlToMarkdown(
            """<img src="image.png" alt="alt text">""",
            "![alt text](image.png)");
    }

    [Fact]
    public void Image_WithAltAndTitle()
    {
        AssertHtmlToMarkdown(
            """<img src="image.png" alt="alt" title="Title">""",
            """![alt](image.png "Title")""");
    }

    [Fact]
    public void Image_NoAlt()
    {
        AssertHtmlToMarkdown(
            """<img src="image.png">""",
            "![](image.png)");
    }

    // --- Horizontal rules ---

    [Fact]
    public void HorizontalRule_Hr()
    {
        AssertHtmlToMarkdown(
            "<hr>",
            "---");
    }

    [Fact]
    public void HorizontalRule_HrSelfClosing()
    {
        AssertHtmlToMarkdown(
            "<hr/>",
            "---");
    }

    [Fact]
    public void HorizontalRule_HrSelfClosingSpace()
    {
        AssertHtmlToMarkdown(
            "<hr />",
            "---");
    }

    [Fact]
    public void HorizontalRule_CustomStyle()
    {
        AssertHtmlToMarkdown(
            "<hr>",
            "***",
            options: new() { ThematicBreak = "***" });
    }

    // --- Unordered lists ---

    [Fact]
    public void UnorderedList_Simple()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>Item 1</li>
                <li>Item 2</li>
                <li>Item 3</li>
            </ul>
            """,
            """
            - Item 1
            - Item 2
            - Item 3
            """);
    }

    [Fact]
    public void UnorderedList_AsteriskMarker()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>Item 1</li>
                <li>Item 2</li>
            </ul>
            """,
            """
            * Item 1
            * Item 2
            """,
            options: new() { UnorderedListMarker = '*' });
    }

    [Fact]
    public void UnorderedList_PlusMarker()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>Item 1</li>
                <li>Item 2</li>
            </ul>
            """,
            """
            + Item 1
            + Item 2
            """,
            options: new() { UnorderedListMarker = '+' });
    }

    // --- Ordered lists ---

    [Fact]
    public void OrderedList_Simple()
    {
        AssertHtmlToMarkdown(
            """
            <ol>
                <li>First</li>
                <li>Second</li>
                <li>Third</li>
            </ol>
            """,
            """
            1. First
            2. Second
            3. Third
            """);
    }

    [Fact]
    public void OrderedList_WithStartAttribute()
    {
        AssertHtmlToMarkdown(
            """
            <ol start="5">
                <li>Fifth</li>
                <li>Sixth</li>
            </ol>
            """,
            """
            5. Fifth
            6. Sixth
            """);
    }

    // --- Nested lists ---

    [Fact]
    public void NestedList_UnorderedInUnordered()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>Item 1
                    <ul>
                        <li>Nested 1</li>
                        <li>Nested 2</li>
                    </ul>
                </li>
                <li>Item 2</li>
            </ul>
            """,
            """
            - Item 1
              - Nested 1
              - Nested 2
            - Item 2
            """);
    }

    [Fact]
    public void NestedList_OrderedInUnordered()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>Item 1
                    <ol>
                        <li>Nested 1</li>
                        <li>Nested 2</li>
                    </ol>
                </li>
                <li>Item 2</li>
            </ul>
            """,
            """
            - Item 1
              1. Nested 1
              2. Nested 2
            - Item 2
            """);
    }

    [Fact]
    public void NestedList_DeeplyNested()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>L1
                    <ul>
                        <li>L2
                            <ul>
                                <li>L3</li>
                            </ul>
                        </li>
                    </ul>
                </li>
            </ul>
            """,
            """
            - L1
              - L2
                - L3
            """);
    }

    // --- Loose lists (items containing paragraphs) ---

    [Fact]
    public void LooseList()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li><p>Item 1</p></li>
                <li><p>Item 2</p></li>
            </ul>
            """,
            """
            - Item 1

            - Item 2
            """);
    }

    // --- Task lists ---

    [Fact]
    public void TaskList_Unchecked()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li><input type="checkbox" disabled> Task 1</li>
            </ul>
            """,
            "- [ ] Task 1");
    }

    [Fact]
    public void TaskList_Checked()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li><input type="checkbox" checked disabled> Task 1</li>
            </ul>
            """,
            "- [x] Task 1");
    }

    [Fact]
    public void TaskList_Mixed()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li><input type="checkbox" disabled> Todo</li>
                <li><input type="checkbox" checked disabled> Done</li>
                <li>Regular item</li>
            </ul>
            """,
            """
            - [ ] Todo
            - [x] Done
            - Regular item
            """);
    }

    // --- Blockquotes ---

    [Fact]
    public void Blockquote_Simple()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <p>Quote</p>
            </blockquote>
            """,
            "> Quote");
    }

    [Fact]
    public void Blockquote_MultiParagraph()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <p>First</p>
                <p>Second</p>
            </blockquote>
            """,
            """
            > First
            >
            > Second
            """);
    }

    [Fact]
    public void Blockquote_Nested()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <blockquote>
                    <p>Nested</p>
                </blockquote>
            </blockquote>
            """,
            "> > Nested");
    }

    [Fact]
    public void Blockquote_WithList()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <ul>
                    <li>Item 1</li>
                    <li>Item 2</li>
                </ul>
            </blockquote>
            """,
            """
            > - Item 1
            > - Item 2
            """);
    }

    // --- Code blocks ---

    [Fact]
    public void CodeBlock_Fenced()
    {
        AssertHtmlToMarkdown(
            "<pre><code>var x = 1;\nvar y = 2;</code></pre>",
            """
            ```
            var x = 1;
            var y = 2;
            ```
            """);
    }

    [Fact]
    public void CodeBlock_FencedWithLanguage()
    {
        AssertHtmlToMarkdown(
            """<pre><code class="language-javascript">console.log('hello');</code></pre>""",
            """
            ```javascript
            console.log('hello');
            ```
            """);
    }

    [Fact]
    public void CodeBlock_FencedWithBackticksInContent()
    {
        AssertHtmlToMarkdown(
            "<pre><code>use ``` for code</code></pre>",
            """
            ````
            use ``` for code
            ````
            """);
    }

    [Fact]
    public void CodeBlock_Indented()
    {
        AssertHtmlToMarkdown(
            "<pre><code>var x = 1;\nvar y = 2;</code></pre>",
            "    var x = 1;\n    var y = 2;",
            options: new() { CodeBlockStyle = CodeBlockStyle.Indented });
    }

    [Fact]
    public void CodeBlock_PreWithoutCode()
    {
        AssertHtmlToMarkdown(
            "<pre>preformatted text</pre>",
            """
            ```
            preformatted text
            ```
            """);
    }

    // --- Tables ---

    [Fact]
    public void Table_Simple()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>Header 1</th>
                        <th>Header 2</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Cell 1</td>
                        <td>Cell 2</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Header 1 | Header 2 |
            | --- | --- |
            | Cell 1 | Cell 2 |
            """);
    }

    [Fact]
    public void Table_WithAlignment()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th align="left">Left</th>
                        <th align="center">Center</th>
                        <th align="right">Right</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td align="left">1</td>
                        <td align="center">2</td>
                        <td align="right">3</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Left | Center | Right |
            | :--- | :---: | ---: |
            | 1 | 2 | 3 |
            """);
    }

    [Fact]
    public void Table_WithoutThead()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <tr>
                    <td>Cell 1</td>
                    <td>Cell 2</td>
                </tr>
                <tr>
                    <td>Cell 3</td>
                    <td>Cell 4</td>
                </tr>
            </table>
            """,
            """
            | Cell 1 | Cell 2 |
            | --- | --- |
            | Cell 3 | Cell 4 |
            """);
    }

    [Fact]
    public void Table_WithFormattedContent()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>Name</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td><strong>Bold</strong></td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Name |
            | --- |
            | **Bold** |
            """);
    }

    [Fact]
    public void Table_EscapePipeInContent()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>Header</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>a | b</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Header |
            | --- |
            | a \| b |
            """);
    }

    [Fact]
    public void Table_WithClassAndWidthStyleAttributes()
    {
        AssertHtmlToMarkdown(
            """
            <table class="table-args">
            <thead>
            <tr>
            <th style="width: 25%;">Name</th>
            <th style="width: 25%;">Type</th>
            <th>Description</th>
            </tr>
            </thead>
            <tbody>
            <tr>
            <td style="width: 25%;"><code>token</code></td>
            <td style="width: 25%;"><code>Token</code></td>
            <td>Auth token passed as an HTTP header.</td>
            </tr>
            <tr>
            <td><code>identifier</code></td>
            <td><code>Text</code></td>
            <td>The user defined unique identifier for the entity.</td>
            </tr>
            <tr>
            <td><code>type</code></td>
            <td><code>Text</code></td>
            <td>The identifier of the entity type.</td>
            </tr>
            </tbody>
            </table>
            """,
            """
            | Name | Type | Description |
            | --- | --- | --- |
            | `token` | `Token` | Auth token passed as an HTTP header. |
            | `identifier` | `Text` | The user defined unique identifier for the entity. |
            | `type` | `Text` | The identifier of the entity type. |
            """);
    }

    // --- Table alignment via CSS text-align ---

    [Fact]
    public void Table_CssTextAlign()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th style="text-align: left">Left</th>
                        <th style="text-align: center">Center</th>
                        <th style="text-align: right">Right</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>1</td>
                        <td>2</td>
                        <td>3</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Left | Center | Right |
            | :--- | :---: | ---: |
            | 1 | 2 | 3 |
            """);
    }

    [Fact]
    public void Table_CssTextAlignNoSpaces()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th style="text-align:center">H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | :---: |
            | D |
            """);
    }

    [Fact]
    public void Table_CssTextAlignWithOtherProperties()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th style="color: red; text-align: right; font-weight: bold">H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | ---: |
            | D |
            """);
    }

    [Fact]
    public void Table_CssTextAlignImportant()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th style="text-align: center !important">H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | :---: |
            | D |
            """);
    }

    [Fact]
    public void Table_CssTextAlignOverridesAlignAttribute()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th align="left" style="text-align: right">H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | ---: |
            | D |
            """);
    }

    [Fact]
    public void Table_CssTextAlignOnTd()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td style="text-align: center">D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | --- |
            | D |
            """);
    }

    [Fact]
    public void Table_CssTextAlignInvalidValue()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th style="text-align: justify">H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | --- |
            | D |
            """);
    }

    // --- Table thead/tbody/tfoot structure ---

    [Fact]
    public void Table_TheadAndTbody()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H1</th>
                        <th>H2</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>A</td>
                        <td>B</td>
                    </tr>
                    <tr>
                        <td>C</td>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H1 | H2 |
            | --- | --- |
            | A | B |
            | C | D |
            """);
    }

    [Fact]
    public void Table_TheadTbodyTfoot()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>Name</th>
                        <th>Score</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Alice</td>
                        <td>90</td>
                    </tr>
                    <tr>
                        <td>Bob</td>
                        <td>85</td>
                    </tr>
                </tbody>
                <tfoot>
                    <tr>
                        <td>Total</td>
                        <td>175</td>
                    </tr>
                </tfoot>
            </table>
            """,
            """
            | Name | Score |
            | --- | --- |
            | Alice | 90 |
            | Bob | 85 |
            | Total | 175 |
            """);
    }

    [Fact]
    public void Table_TbodyOnly()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <tbody>
                    <tr>
                        <td>A</td>
                        <td>B</td>
                    </tr>
                    <tr>
                        <td>C</td>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | A | B |
            | --- | --- |
            | C | D |
            """);
    }

    [Fact]
    public void Table_MultipleTbody()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>A</td>
                    </tr>
                </tbody>
                <tbody>
                    <tr>
                        <td>B</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | --- |
            | A |
            | B |
            """);
    }

    [Fact]
    public void Table_MixedDirectRowsAndTbodyRows()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H</th>
                    </tr>
                </thead>
                <tr>
                    <td>A</td>
                </tr>
                <tbody>
                    <tr>
                        <td>B</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | --- |
            | A |
            | B |
            """);
    }

    [Fact]
    public void Table_TfootOnly()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H</th>
                    </tr>
                </thead>
                <tfoot>
                    <tr>
                        <td>Footer</td>
                    </tr>
                </tfoot>
            </table>
            """,
            """
            | H |
            | --- |
            | Footer |
            """);
    }

    [Fact]
    public void Table_TfootBeforeTbody()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H</th>
                    </tr>
                </thead>
                <tfoot>
                    <tr>
                        <td>Foot</td>
                    </tr>
                </tfoot>
                <tbody>
                    <tr>
                        <td>Body</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | --- |
            | Body |
            | Foot |
            """);
    }

    [Fact]
    public void Table_NoTheadFirstRowIsTh()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <tr>
                    <th>H1</th>
                    <th>H2</th>
                </tr>
                <tr>
                    <td>A</td>
                    <td>B</td>
                </tr>
            </table>
            """,
            """
            | H1 | H2 |
            | --- | --- |
            | A | B |
            """);
    }

    [Fact]
    public void Table_TheadWithAlignment_TbodyInheritsNone()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th style="text-align: center">Name</th>
                        <th style="text-align: right">Value</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Item</td>
                        <td>42</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Name | Value |
            | :---: | ---: |
            | Item | 42 |
            """);
    }

    [Fact]
    public void Table_EmptyThead()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead></thead>
                <tbody>
                    <tr>
                        <td>A</td>
                        <td>B</td>
                    </tr>
                    <tr>
                        <td>C</td>
                        <td>D</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | A | B |
            | --- | --- |
            | C | D |
            """);
    }

    [Fact]
    public void Table_MixedAlignAttributeAndCss()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th align="left">Col1</th>
                        <th style="text-align: center">Col2</th>
                        <th>Col3</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>A</td>
                        <td>B</td>
                        <td>C</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Col1 | Col2 | Col3 |
            | :--- | :---: | --- |
            | A | B | C |
            """);
    }

    // --- Line breaks ---

    [Fact]
    public void LineBreak_Br()
    {
        AssertHtmlToMarkdown(
            "Line 1<br>Line 2",
            "Line 1  \nLine 2");
    }

    [Fact]
    public void LineBreak_BrSelfClosing()
    {
        AssertHtmlToMarkdown(
            "Line 1<br/>Line 2",
            "Line 1  \nLine 2");
    }

    [Fact]
    public void LineBreak_BrSelfClosingSpace()
    {
        AssertHtmlToMarkdown(
            "Line 1<br />Line 2",
            "Line 1  \nLine 2");
    }

    [Fact]
    public void LineBreak_Backslash()
    {
        AssertHtmlToMarkdown(
            "Line 1<br>Line 2",
            "Line 1\\\nLine 2",
            options: new() { LineBreakStyle = LineBreakStyle.Backslash });
    }

    // --- Character escaping ---

    [Fact]
    public void CharacterEscaping_Asterisks()
    {
        AssertHtmlToMarkdown(
            "<p>*not italic*</p>",
            "\\*not italic\\*");
    }

    [Fact]
    public void CharacterEscaping_Underscores()
    {
        AssertHtmlToMarkdown(
            "<p>_not italic_</p>",
            "\\_not italic\\_");
    }

    [Fact]
    public void CharacterEscaping_Hash()
    {
        AssertHtmlToMarkdown(
            "<p># not a heading</p>",
            "\\# not a heading");
    }

    [Fact]
    public void CharacterEscaping_Brackets()
    {
        AssertHtmlToMarkdown(
            "<p>[not a link](url)</p>",
            "\\[not a link\\](url)");
    }

    [Fact]
    public void CharacterEscaping_Backticks()
    {
        AssertHtmlToMarkdown(
            "<p>`not code`</p>",
            "\\`not code\\`");
    }

    [Fact]
    public void CharacterEscaping_OrderedListMarker()
    {
        AssertHtmlToMarkdown(
            "<p>1. not a list</p>",
            "1\\. not a list");
    }

    [Fact]
    public void CharacterEscaping_Dash()
    {
        AssertHtmlToMarkdown(
            "<p>- not a list</p>",
            "\\- not a list");
    }

    [Fact]
    public void CharacterEscaping_GreaterThan()
    {
        AssertHtmlToMarkdown(
            "<p>> not a quote</p>",
            "\\> not a quote");
    }

    // --- Semantic / structural HTML elements ---

    [Fact]
    public void SemanticElement_Div()
    {
        AssertHtmlToMarkdown(
            "<div>content</div>",
            "content");
    }

    [Fact]
    public void SemanticElement_Article()
    {
        AssertHtmlToMarkdown(
            "<article>content</article>",
            "content");
    }

    [Fact]
    public void SemanticElement_Section()
    {
        AssertHtmlToMarkdown(
            "<section>content</section>",
            "content");
    }

    [Fact]
    public void SemanticElement_Nav()
    {
        AssertHtmlToMarkdown(
            "<nav>content</nav>",
            "content");
    }

    [Fact]
    public void SemanticElement_Main()
    {
        AssertHtmlToMarkdown(
            "<main>content</main>",
            "content");
    }

    [Fact]
    public void SemanticElement_Header()
    {
        AssertHtmlToMarkdown(
            "<header>content</header>",
            "content");
    }

    [Fact]
    public void SemanticElement_Footer()
    {
        AssertHtmlToMarkdown(
            "<footer>content</footer>",
            "content");
    }

    [Fact]
    public void SemanticElement_Aside()
    {
        AssertHtmlToMarkdown(
            "<aside>content</aside>",
            "content");
    }

    [Fact]
    public void SpanElement_ContentPreserved()
    {
        AssertHtmlToMarkdown(
            "<span>inline</span>",
            "inline");
    }

    // --- Stripped elements ---

    [Fact]
    public void StrippedElement_Script()
    {
        AssertHtmlToMarkdown(
            "<script>alert('xss')</script>",
            "");
    }

    [Fact]
    public void StrippedElement_Style()
    {
        AssertHtmlToMarkdown(
            "<style>.red { color: red; }</style>",
            "");
    }

    [Fact]
    public void StrippedElement_Noscript()
    {
        AssertHtmlToMarkdown(
            "<noscript>Enable JS</noscript>",
            "");
    }

    [Fact]
    public void StrippedElements_SurroundingContent()
    {
        AssertHtmlToMarkdown(
            "before <script>alert(1)</script> after",
            "before after");
    }

    // --- Unknown element handling ---

    [Fact]
    public void UnknownElement_PassThrough()
    {
        AssertHtmlToMarkdown(
            """<video src="video.mp4">fallback</video>""",
            """<video src="video.mp4">fallback</video>""",
            options: new() { UnknownElementHandling = UnknownElementHandling.PassThrough });
    }

    [Fact]
    public void UnknownElement_Strip()
    {
        AssertHtmlToMarkdown(
            """<video src="video.mp4">fallback</video>""",
            "",
            options: new() { UnknownElementHandling = UnknownElementHandling.Strip });
    }

    [Fact]
    public void UnknownElement_StripKeepContent()
    {
        AssertHtmlToMarkdown(
            """<video src="video.mp4">fallback</video>""",
            "fallback",
            options: new() { UnknownElementHandling = UnknownElementHandling.StripKeepContent });
    }

    // --- HTML entities ---

    [Fact]
    public void HtmlEntity_Amp()
    {
        AssertHtmlToMarkdown(
            "&amp;",
            "&");
    }

    [Fact]
    public void HtmlEntity_Lt()
    {
        AssertHtmlToMarkdown(
            "&lt;",
            "\\<");
    }

    [Fact]
    public void HtmlEntity_Gt()
    {
        AssertHtmlToMarkdown(
            "&gt;",
            "\\>");
    }

    [Fact]
    public void HtmlEntity_Quot()
    {
        AssertHtmlToMarkdown(
            "&quot;",
            "\"");
    }

    // --- Nested and complex structures ---

    [Fact]
    public void NestedStrong_Collapsed()
    {
        AssertHtmlToMarkdown(
            "<strong><strong>bold</strong></strong>",
            "**bold**");
    }

    [Fact]
    public void AdjacentInlineElements()
    {
        AssertHtmlToMarkdown(
            "<strong>bold</strong> and <em>italic</em>",
            "**bold** and *italic*");
    }

    [Fact]
    public void MixedBlockAndInline()
    {
        AssertHtmlToMarkdown(
            """
            <h1>Title</h1>
            <p>Paragraph with <strong>bold</strong> and <a href="url">link</a>.</p>
            """,
            """
            # Title

            Paragraph with **bold** and [link](url).
            """);
    }

    [Fact]
    public void ComplexDocument()
    {
        AssertHtmlToMarkdown(
            """
            <h1>Title</h1>
            <p>Intro paragraph.</p>
            <h2>Section</h2>
            <ul>
                <li>Item 1</li>
                <li>Item 2</li>
            </ul>
            <blockquote>
                <p>A quote.</p>
            </blockquote>
            <pre><code>code</code></pre>
            """,
            """
            # Title

            Intro paragraph.

            ## Section

            - Item 1
            - Item 2

            > A quote.

            ```
            code
            ```
            """);
    }

    // --- Definition lists ---

    [Fact]
    public void DefinitionList()
    {
        AssertHtmlToMarkdown(
            """
            <dl>
                <dt>Term</dt>
                <dd>Definition</dd>
            </dl>
            """,
            """
            Term
            :   Definition
            """);
    }

    // --- Figure / Figcaption ---

    [Fact]
    public void Figure_WithImage()
    {
        AssertHtmlToMarkdown(
            """
            <figure>
                <img src="img.png" alt="alt">
                <figcaption>Caption</figcaption>
            </figure>
            """,
            """
            ![alt](img.png)

            Caption
            """);
    }

    // --- Whitespace handling ---

    [Fact]
    public void Whitespace_ExtraSpacesCollapsed()
    {
        AssertHtmlToMarkdown(
            "<p>hello    world</p>",
            "hello world");
    }

    [Fact]
    public void Whitespace_PreservedInPre()
    {
        AssertHtmlToMarkdown(
            "<pre><code>  indented\n    more indented</code></pre>",
            """
            ```
              indented
                more indented
            ```
            """);
    }

    [Fact]
    public void Whitespace_NoExtraBlankLines()
    {
        AssertHtmlToMarkdown(
            "<p>First</p>\n\n\n\n<p>Second</p>",
            """
            First

            Second
            """);
    }

    // --- Malformed HTML ---

    [Fact]
    public void MalformedHtml_UnclosedTags()
    {
        var result = HtmlToMarkdown.Convert("<p>unclosed paragraph<p>another");
        Assert.NotNull(result);
    }

    [Fact]
    public void MalformedHtml_NestedParagraphs()
    {
        var result = HtmlToMarkdown.Convert("<p><p>nested</p></p>");
        Assert.Contains("nested", result, StringComparison.Ordinal);
    }

    // --- Sub/Sup (pass-through by default) ---

    [Fact]
    public void SubSup_PassThrough_Sub()
    {
        AssertHtmlToMarkdown(
            "<sub>sub</sub>",
            "<sub>sub</sub>");
    }

    [Fact]
    public void SubSup_PassThrough_Sup()
    {
        AssertHtmlToMarkdown(
            "<sup>sup</sup>",
            "<sup>sup</sup>");
    }

    [Fact]
    public void SubSup_StripKeepContent_Sub()
    {
        AssertHtmlToMarkdown(
            "<sub>sub</sub>",
            "sub",
            options: new() { UnknownElementHandling = UnknownElementHandling.StripKeepContent });
    }

    [Fact]
    public void SubSup_StripKeepContent_Sup()
    {
        AssertHtmlToMarkdown(
            "<sup>sup</sup>",
            "sup",
            options: new() { UnknownElementHandling = UnknownElementHandling.StripKeepContent });
    }

    // =========================================================================
    // Complex / edge-case tests
    // =========================================================================

    // --- Inline code edge cases ---

    [Fact]
    public void InlineCode_SingleBacktick()
    {
        AssertHtmlToMarkdown(
            "<code>`</code>",
            "`` ` ``");
    }

    [Fact]
    public void InlineCode_MultipleConsecutiveBackticks()
    {
        AssertHtmlToMarkdown(
            "<code>``</code>",
            "``` `` ```");
    }

    [Fact]
    public void InlineCode_ThreeBackticksInMiddle()
    {
        AssertHtmlToMarkdown(
            "<code>use ``` for code</code>",
            "````use ``` for code````");
    }

    [Fact]
    public void InlineCode_MixedBacktickLengths()
    {
        AssertHtmlToMarkdown(
            "<code>`one` and ``two``</code>",
            "``` `one` and ``two`` ```");
    }

    [Fact]
    public void InlineCode_LeadingAndTrailingSpaces()
    {
        AssertHtmlToMarkdown(
            "<code>a b</code>",
            "`a b`");
    }

    [Fact]
    public void InlineCode_EmptyContent()
    {
        AssertHtmlToMarkdown(
            "<code></code>",
            "");
    }

    [Fact]
    public void InlineCode_OnlySpaces()
    {
        AssertHtmlToMarkdown(
            "<code> </code>",
            "` `");
    }

    // --- Code blocks with backticks/tildes in content ---

    [Fact]
    public void CodeBlock_ContentContainsTripleBackticks()
    {
        AssertHtmlToMarkdown(
            "<pre><code>```\nsome code\n```</code></pre>",
            """
            ````
            ```
            some code
            ```
            ````
            """);
    }

    [Fact]
    public void CodeBlock_ContentContainsQuadrupleBackticks()
    {
        AssertHtmlToMarkdown(
            "<pre><code>````\nsome code\n````</code></pre>",
            """
            `````
            ````
            some code
            ````
            `````
            """);
    }

    [Fact]
    public void CodeBlock_ContentContainsMixedBacktickRuns()
    {
        AssertHtmlToMarkdown(
            "<pre><code>``` and `````</code></pre>",
            """
            ``````
            ``` and `````
            ``````
            """);
    }

    [Fact]
    public void CodeBlock_ContentContainsTildes_WithTildeFence()
    {
        AssertHtmlToMarkdown(
            "<pre><code>~~~ not a fence</code></pre>",
            """
            ~~~~
            ~~~ not a fence
            ~~~~
            """,
            options: new() { CodeBlockFenceCharacter = '~' });
    }

    [Fact]
    public void CodeBlock_EmptyContent()
    {
        AssertHtmlToMarkdown(
            "<pre><code></code></pre>",
            """
            ```

            ```
            """);
    }

    [Fact]
    public void CodeBlock_SingleBlankLine()
    {
        AssertHtmlToMarkdown(
            "<pre><code>\n</code></pre>",
            """
            ```

            ```
            """);
    }

    [Fact]
    public void CodeBlock_PreservesLeadingWhitespace()
    {
        AssertHtmlToMarkdown(
            "<pre><code>    indented\n  also indented\nnot indented</code></pre>",
            """
            ```
                indented
              also indented
            not indented
            ```
            """);
    }

    [Fact]
    public void CodeBlock_PreservesBlankLines()
    {
        AssertHtmlToMarkdown(
            "<pre><code>line 1\n\nline 3\n\n\nline 6</code></pre>",
            "```\nline 1\n\nline 3\n\n\nline 6\n```");
    }

    [Fact]
    public void CodeBlock_LanguageClass_DashInName()
    {
        AssertHtmlToMarkdown(
            """<pre><code class="language-c-sharp">var x = 1;</code></pre>""",
            """
            ```c-sharp
            var x = 1;
            ```
            """);
    }

    [Fact]
    public void CodeBlock_MultipleClasses_LanguageExtracted()
    {
        AssertHtmlToMarkdown(
            """<pre><code class="highlight language-python">print('hi')</code></pre>""",
            """
            ```python
            print('hi')
            ```
            """);
    }

    // --- Code block inside nested list ---

    [Fact]
    public void NestedList_WithCodeBlock()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>Item 1<pre><code>code here</code></pre></li>
                <li>Item 2</li>
            </ul>
            """,
            """
            - Item 1

              ```
              code here
              ```

            - Item 2
            """);
    }

    [Fact]
    public void NestedList_WithCodeBlockContainingBackticks()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>Example:<pre><code>use ``` for fenced code</code></pre></li>
            </ul>
            """,
            """
            - Example:

              ````
              use ``` for fenced code
              ````
            """);
    }

    [Fact]
    public void DeeplyNestedList_WithCodeBlock()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>L1
                    <ul>
                        <li>L2<pre><code>nested code</code></pre></li>
                    </ul>
                </li>
            </ul>
            """,
            """
            - L1
              - L2

                ```
                nested code
                ```
            """);
    }

    [Fact]
    public void OrderedList_WithCodeBlock()
    {
        AssertHtmlToMarkdown(
            """
            <ol>
                <li>
                    <p>Step one</p>
                    <pre><code class="language-bash">echo hello</code></pre>
                </li>
                <li><p>Step two</p></li>
            </ol>
            """,
            """
            1. Step one

               ```bash
               echo hello
               ```

            2. Step two
            """);
    }

    // --- Blockquote complex scenarios ---

    [Fact]
    public void Blockquote_WithCodeBlock()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <pre><code>code in quote</code></pre>
            </blockquote>
            """,
            """
            > ```
            > code in quote
            > ```
            """);
    }

    [Fact]
    public void Blockquote_WithHeadingAndParagraph()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <h2>Title</h2>
                <p>Some text</p>
            </blockquote>
            """,
            """
            > ## Title
            >
            > Some text
            """);
    }

    [Fact]
    public void Blockquote_ThreeLevelsDeep()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <blockquote>
                    <blockquote>
                        <p>Deep</p>
                    </blockquote>
                </blockquote>
            </blockquote>
            """,
            "> > > Deep");
    }

    [Fact]
    public void Blockquote_WithNestedList()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <ol>
                    <li>First
                        <ul>
                            <li>Nested</li>
                        </ul>
                    </li>
                    <li>Second</li>
                </ol>
            </blockquote>
            """,
            """
            > 1. First
            >    - Nested
            > 2. Second
            """);
    }

    [Fact]
    public void Blockquote_NestedWithCodeBlock()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <blockquote>
                    <pre><code>nested code</code></pre>
                </blockquote>
            </blockquote>
            """,
            """
            > > ```
            > > nested code
            > > ```
            """);
    }

    // --- Table complex scenarios ---

    [Fact]
    public void Table_SingleColumn()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>Only</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Cell</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Only |
            | --- |
            | Cell |
            """);
    }

    [Fact]
    public void Table_EmptyCells()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H1</th>
                        <th>H2</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td></td>
                        <td>data</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H1 | H2 |
            | --- | --- |
            |  | data |
            """);
    }

    [Fact]
    public void Table_CellWithLink()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>Name</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td><a href="https://example.com">Link</a></td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Name |
            | --- |
            | [Link](https://example.com) |
            """);
    }

    [Fact]
    public void Table_CellWithInlineCode()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>Code</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td><code>var x = 1</code></td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Code |
            | --- |
            | `var x = 1` |
            """);
    }

    [Fact]
    public void Table_CellWithLineBreak()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>line1<br>line2</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | --- |
            | line1<br>line2 |
            """);
    }

    [Fact]
    public void Table_ManyColumns()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>A</th>
                        <th>B</th>
                        <th>C</th>
                        <th>D</th>
                        <th>E</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>1</td>
                        <td>2</td>
                        <td>3</td>
                        <td>4</td>
                        <td>5</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | A | B | C | D | E |
            | --- | --- | --- | --- | --- |
            | 1 | 2 | 3 | 4 | 5 |
            """);
    }

    [Fact]
    public void Table_MultipleBodyRows()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <thead>
                    <tr>
                        <th>H</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>R1</td>
                    </tr>
                    <tr>
                        <td>R2</td>
                    </tr>
                    <tr>
                        <td>R3</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | H |
            | --- |
            | R1 |
            | R2 |
            | R3 |
            """);
    }

    [Fact]
    public void Table_IgnoresRowsFromNestedTables()
    {
        AssertHtmlToMarkdown(
            """
            <table>
                <caption>
                    <table>
                        <tbody>
                            <tr>
                                <td>Nested row</td>
                            </tr>
                        </tbody>
                    </table>
                </caption>
                <thead>
                    <tr>
                        <th>Outer</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Outer row</td>
                    </tr>
                </tbody>
            </table>
            """,
            """
            | Outer |
            | --- |
            | Outer row |
            """);
    }

    // --- Nested inline formatting edge cases ---

    [Fact]
    public void Emphasis_InsideStrong()
    {
        AssertHtmlToMarkdown(
            "<strong>bold <em>and italic</em> here</strong>",
            "**bold *and italic* here**");
    }

    [Fact]
    public void Strong_InsideEmphasis()
    {
        AssertHtmlToMarkdown(
            "<em>italic <strong>and bold</strong> here</em>",
            "*italic **and bold** here*");
    }

    [Fact]
    public void Strikethrough_WithEmphasis()
    {
        AssertHtmlToMarkdown(
            "<del><em>deleted italic</em></del>",
            "~~*deleted italic*~~");
    }

    [Fact]
    public void Link_ContainingCode()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com"><code>code</code></a>""",
            "[`code`](https://example.com)");
    }

    [Fact]
    public void Link_ContainingImage()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com"><img src="img.png" alt="alt"></a>""",
            "[![alt](img.png)](https://example.com)");
    }

    [Fact]
    public void Link_ContainingEmphasisAndCode()
    {
        AssertHtmlToMarkdown(
            """<a href="url"><strong>bold</strong> and <code>code</code></a>""",
            "[**bold** and `code`](url)");
    }

    [Fact]
    public void Emphasis_SurroundingLink()
    {
        AssertHtmlToMarkdown(
            """<em><a href="url">link text</a></em>""",
            "*[link text](url)*");
    }

    [Fact]
    public void Strong_SurroundingLink()
    {
        AssertHtmlToMarkdown(
            """<strong><a href="url">link text</a></strong>""",
            "**[link text](url)**");
    }

    // --- Lists complex edge cases ---

    [Fact]
    public void List_ItemWithMultipleParagraphs()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>
                    <p>First paragraph</p>
                    <p>Second paragraph</p>
                </li>
                <li><p>Another item</p></li>
            </ul>
            """,
            """
            - First paragraph

              Second paragraph

            - Another item
            """);
    }

    [Fact]
    public void List_ItemWithBlockquote()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>
                    <p>Item</p>
                    <blockquote><p>Quoted</p></blockquote>
                </li>
            </ul>
            """,
            """
            - Item

              > Quoted
            """);
    }

    [Fact]
    public void List_ItemWithHeading()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>
                    <h3>Heading in list</h3>
                    <p>Text after heading</p>
                </li>
            </ul>
            """,
            """
            - ### Heading in list

              Text after heading
            """);
    }

    [Fact]
    public void OrderedList_LargeNumbers()
    {
        AssertHtmlToMarkdown(
            """
            <ol start="99">
                <li>Ninety-nine</li>
                <li>One hundred</li>
            </ol>
            """,
            """
            99. Ninety-nine
            100. One hundred
            """);
    }

    [Fact]
    public void NestedList_FourLevelsDeep()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>L1
                    <ul>
                        <li>L2
                            <ul>
                                <li>L3
                                    <ul>
                                        <li>L4</li>
                                    </ul>
                                </li>
                            </ul>
                        </li>
                    </ul>
                </li>
            </ul>
            """,
            """
            - L1
              - L2
                - L3
                  - L4
            """);
    }

    [Fact]
    public void NestedList_MixedOrderedUnorderedDeeply()
    {
        AssertHtmlToMarkdown(
            """
            <ol>
                <li>Ordered 1
                    <ul>
                        <li>Unordered A
                            <ol>
                                <li>Ordered i</li>
                                <li>Ordered ii</li>
                            </ol>
                        </li>
                    </ul>
                </li>
                <li>Ordered 2</li>
            </ol>
            """,
            """
            1. Ordered 1
               - Unordered A
                 1. Ordered i
                 2. Ordered ii
            2. Ordered 2
            """);
    }

    [Fact]
    public void List_ItemWithInlineFormatting()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li><strong>Bold</strong> item with <em>italic</em> and <code>code</code></li>
            </ul>
            """,
            "- **Bold** item with *italic* and `code`");
    }

    [Fact]
    public void List_ItemWithLink()
    {
        AssertHtmlToMarkdown(
            """
            <ol>
                <li>See <a href="https://example.com">this link</a> for details</li>
            </ol>
            """,
            "1. See [this link](https://example.com) for details");
    }

    // --- Character escaping edge cases ---

    [Fact]
    public void CharacterEscaping_PercentSign()
    {
        AssertHtmlToMarkdown(
            "<p>100% done</p>",
            "100% done");
    }

    [Fact]
    public void CharacterEscaping_DollarSign()
    {
        AssertHtmlToMarkdown(
            "<p>price: $100</p>",
            "price: $100");
    }

    [Fact]
    public void CharacterEscaping_Pipes()
    {
        AssertHtmlToMarkdown(
            "<p>a | b | c</p>",
            "a \\| b \\| c");
    }

    [Fact]
    public void CharacterEscaping_Tildes()
    {
        AssertHtmlToMarkdown(
            "<p>~~not strikethrough~~</p>",
            "\\~\\~not strikethrough\\~\\~");
    }

    [Fact]
    public void CharacterEscaping_TripleDash()
    {
        AssertHtmlToMarkdown(
            "<p>use --- for hr</p>",
            "use \\-\\-\\- for hr");
    }

    [Fact]
    public void CharacterEscaping_BackslashInText()
    {
        AssertHtmlToMarkdown(
            "<p>path\\to\\file</p>",
            "path\\\\to\\\\file");
    }

    [Fact]
    public void CharacterEscaping_UrlsNotEscaped()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com/path?a=1&amp;b=2#section">text</a>""",
            "[text](https://example.com/path?a=1&b=2#section)");
    }

    [Fact]
    public void CharacterEscaping_InsideCodeNotEscaped()
    {
        AssertHtmlToMarkdown(
            "<code>*not emphasis*</code>",
            "`*not emphasis*`");
    }

    [Fact]
    public void CharacterEscaping_ImageAltTextNotEscaped()
    {
        AssertHtmlToMarkdown(
            """<img src="star.png" alt="Photo of a * star">""",
            "![Photo of a * star](star.png)");
    }

    // --- Links edge cases ---

    [Fact]
    public void Link_WithSpecialCharsInText()
    {
        AssertHtmlToMarkdown(
            """<a href="url">text [with brackets]</a>""",
            "[text \\[with brackets\\]](url)");
    }

    [Fact]
    public void Link_UrlWithSpaces()
    {
        AssertHtmlToMarkdown(
            """<a href="https://example.com/my%20page">text</a>""",
            "[text](https://example.com/my%20page)");
    }

    [Fact]
    public void Link_UrlWithQuotesInTitle()
    {
        AssertHtmlToMarkdown(
            """<a href="url" title="a &quot;quoted&quot; title">text</a>""",
            """[text](url "a \"quoted\" title")""");
    }

    [Fact]
    public void Link_NestedAnchors_InnerLinkOnly()
    {
        var result = HtmlToMarkdown.Convert("""<a href="outer">text <a href="inner">inner</a> more</a>""");
        Assert.NotNull(result);
    }

    // --- Image edge cases ---

    [Fact]
    public void Image_AltWithSpecialChars()
    {
        AssertHtmlToMarkdown(
            """<img src="img.png" alt="a &quot;quoted&quot; image">""",
            """![a "quoted" image](img.png)""");
    }

    [Fact]
    public void Image_NoSrc()
    {
        AssertHtmlToMarkdown(
            """<img alt="orphan">""",
            "");
    }

    [Fact]
    public void Image_InParagraph()
    {
        AssertHtmlToMarkdown(
            """<p>Before <img src="img.png" alt="alt"> after</p>""",
            "Before ![alt](img.png) after");
    }

    // --- Whitespace edge cases ---

    [Fact]
    public void Whitespace_TabsCollapsedOutsidePre()
    {
        AssertHtmlToMarkdown(
            "<p>hello\t\tworld</p>",
            "hello world");
    }

    [Fact]
    public void Whitespace_NewlinesCollapsedOutsidePre()
    {
        AssertHtmlToMarkdown(
            "<p>hello\n\nworld</p>",
            "hello world");
    }

    [Fact]
    public void Whitespace_PreservesTabsInPre()
    {
        AssertHtmlToMarkdown(
            "<pre><code>if true:\n\tindented</code></pre>",
            "```\nif true:\n\tindented\n```");
    }

    [Fact]
    public void Whitespace_BetweenInlineElements()
    {
        AssertHtmlToMarkdown(
            "<em>a</em> <em>b</em> <em>c</em>",
            "*a* *b* *c*");
    }

    [Fact]
    public void Whitespace_NoSpaceBetweenAdjacentInline()
    {
        AssertHtmlToMarkdown(
            "<em>a</em><em>b</em>",
            "*a**b*");
    }

    // --- Definition list edge cases ---

    [Fact]
    public void DefinitionList_MultipleTerms()
    {
        AssertHtmlToMarkdown(
            """
            <dl>
                <dt>Term 1</dt>
                <dd>Def 1</dd>
                <dt>Term 2</dt>
                <dd>Def 2</dd>
            </dl>
            """,
            """
            Term 1
            :   Def 1

            Term 2
            :   Def 2
            """);
    }

    [Fact]
    public void DefinitionList_MultipleDefinitionsPerTerm()
    {
        AssertHtmlToMarkdown(
            """
            <dl>
                <dt>Term</dt>
                <dd>Def A</dd>
                <dd>Def B</dd>
            </dl>
            """,
            """
            Term
            :   Def A
            :   Def B
            """);
    }

    [Fact]
    public void DefinitionList_WithFormatting()
    {
        AssertHtmlToMarkdown(
            """
            <dl>
                <dt><strong>Bold Term</strong></dt>
                <dd>A <em>definition</em> with formatting</dd>
            </dl>
            """,
            """
            **Bold Term**
            :   A *definition* with formatting
            """);
    }

    // --- Deeply nested complex structures ---

    [Fact]
    public void Blockquote_ContainingList_ContainingCodeBlock()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <ul>
                    <li>Item:<pre><code>code</code></pre></li>
                </ul>
            </blockquote>
            """,
            """
            > - Item:
            >
            >   ```
            >   code
            >   ```
            """);
    }

    [Fact]
    public void Blockquote_ContainingTable()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <table>
                    <thead>
                        <tr>
                            <th>H</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td>D</td>
                        </tr>
                    </tbody>
                </table>
            </blockquote>
            """,
            """
            > | H |
            > | --- |
            > | D |
            """);
    }

    [Fact]
    public void List_ContainingBlockquote_ContainingList()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>
                    <blockquote>
                        <ul>
                            <li>Nested in quote in list</li>
                        </ul>
                    </blockquote>
                </li>
            </ul>
            """,
            "- > - Nested in quote in list");
    }

    [Fact]
    public void ComplexDocument_AllFeatures()
    {
        AssertHtmlToMarkdown(
            """
            <h1>Project README</h1>
            <p>This is a <strong>complex</strong> document with <em>many</em> features.</p>
            <h2>Installation</h2>
            <pre><code class="language-bash">npm install my-package</code></pre>
            <h2>Usage</h2>
            <ol>
                <li>
                    <p>Import the module:</p>
                    <pre><code class="language-js">import { foo } from 'bar';</code></pre>
                </li>
                <li>
                    <p>Call the function:</p>
                    <pre><code class="language-js">foo();</code></pre>
                </li>
            </ol>
            <blockquote>
                <p><strong>Note:</strong> This is important.</p>
            </blockquote>
            <h2>Features</h2>
            <table>
                <thead>
                    <tr>
                        <th>Feature</th>
                        <th>Status</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Core</td>
                        <td>Done</td>
                    </tr>
                    <tr>
                        <td>Extras</td>
                        <td><del>Pending</del></td>
                    </tr>
                </tbody>
            </table>
            <h2>Links</h2>
            <ul>
                <li><a href="https://example.com">Homepage</a></li>
                <li><a href="https://docs.example.com">Documentation</a></li>
            </ul>
            """,
            """
            # Project README

            This is a **complex** document with *many* features.

            ## Installation

            ```bash
            npm install my-package
            ```

            ## Usage

            1. Import the module:

               ```js
               import { foo } from 'bar';
               ```

            2. Call the function:

               ```js
               foo();
               ```

            > **Note:** This is important.

            ## Features

            | Feature | Status |
            | --- | --- |
            | Core | Done |
            | Extras | ~~Pending~~ |

            ## Links

            - [Homepage](https://example.com)
            - [Documentation](https://docs.example.com)
            """);
    }

    // --- Multiple block elements separated correctly ---

    [Fact]
    public void BlockSeparation_HeadingAfterParagraph()
    {
        AssertHtmlToMarkdown(
            "<p>text</p><h1>Heading</h1>",
            """
            text

            # Heading
            """);
    }

    [Fact]
    public void BlockSeparation_ListAfterParagraph()
    {
        AssertHtmlToMarkdown(
            """
            <p>text</p>
            <ul>
                <li>item</li>
            </ul>
            """,
            """
            text

            - item
            """);
    }

    [Fact]
    public void BlockSeparation_CodeBlockAfterList()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>item</li>
            </ul>
            <pre><code>code</code></pre>
            """,
            """
            - item

            ```
            code
            ```
            """);
    }

    [Fact]
    public void BlockSeparation_HrBetweenParagraphs()
    {
        AssertHtmlToMarkdown(
            "<p>above</p><hr><p>below</p>",
            """
            above

            ---

            below
            """);
    }

    [Fact]
    public void BlockSeparation_BlockquoteAfterCodeBlock()
    {
        AssertHtmlToMarkdown(
            """
            <pre><code>code</code></pre>
            <blockquote>
                <p>quote</p>
            </blockquote>
            """,
            """
            ```
            code
            ```

            > quote
            """);
    }

    // --- Unicode and special content ---

    [Fact]
    public void Unicode_Emoji()
    {
        AssertHtmlToMarkdown(
            "<p>Hello \ud83d\ude00 world</p>",
            "Hello \ud83d\ude00 world");
    }

    [Fact]
    public void Unicode_CJK()
    {
        AssertHtmlToMarkdown(
            "<p>\u4f60\u597d\u4e16\u754c</p>",
            "\u4f60\u597d\u4e16\u754c");
    }

    [Fact]
    public void Unicode_RTL()
    {
        AssertHtmlToMarkdown(
            "<p>\u0645\u0631\u062d\u0628\u0627</p>",
            "\u0645\u0631\u062d\u0628\u0627");
    }

    [Fact]
    public void NonBreakingSpace()
    {
        var result = HtmlToMarkdown.Convert("<p>hello&nbsp;world</p>");
        Assert.Contains("hello", result, StringComparison.Ordinal);
        Assert.Contains("world", result, StringComparison.Ordinal);
    }

    // --- Smart punctuation ---

    [Fact]
    public void SmartPunctuation_DisabledByDefault()
    {
        AssertHtmlToMarkdown(
            "<p>\"Hello\"</p>",
            "\"Hello\"");
    }

    [Fact]
    public void SmartPunctuation_ReplacesRequestedSequences()
    {
        AssertHtmlToMarkdown(
            "<p>\"Hello\" 'Hello' --- -- ... << >></p>",
            "“Hello” ‘Hello’ — – … « »",
            options: new() { UseSmartPunctuation = true });
    }

    [Fact]
    public void SmartPunctuation_WorksAcrossInlineNodes()
    {
        AssertHtmlToMarkdown(
            "<p>\"Hello <em>world</em>\"</p>",
            "“Hello *world*”",
            options: new() { UseSmartPunctuation = true });
    }

    [Fact]
    public void SmartPunctuation_DoesNotAffectInlineCode()
    {
        AssertHtmlToMarkdown(
            "<p><code>\"Hello\" -- ... << >></code></p>",
            "`\"Hello\" -- ... << >>`",
            options: new() { UseSmartPunctuation = true });
    }

    [Fact]
    public void SmartPunctuation_DoesNotAffectCodeBlocks()
    {
        AssertHtmlToMarkdown(
            "<pre><code>\"Hello\" -- ... << >></code></pre>",
            "```\n\"Hello\" -- ... << >>\n```",
            options: new() { UseSmartPunctuation = true });
    }

    // --- Consecutive same-type elements ---

    [Fact]
    public void ConsecutiveBlockquotes()
    {
        AssertHtmlToMarkdown(
            """
            <blockquote>
                <p>Quote 1</p>
            </blockquote>
            <blockquote>
                <p>Quote 2</p>
            </blockquote>
            """,
            """
            > Quote 1

            > Quote 2
            """);
    }

    [Fact]
    public void ConsecutiveCodeBlocks()
    {
        AssertHtmlToMarkdown(
            """
            <pre><code>block 1</code></pre>
            <pre><code>block 2</code></pre>
            """,
            """
            ```
            block 1
            ```

            ```
            block 2
            ```
            """);
    }

    [Fact]
    public void ConsecutiveLists()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li>A</li>
            </ul>
            <ul>
                <li>B</li>
            </ul>
            """,
            """
            - A

            - B
            """);
    }

    [Fact]
    public void ConsecutiveHeadings()
    {
        AssertHtmlToMarkdown(
            "<h1>Title</h1><h2>Subtitle</h2>",
            """
            # Title

            ## Subtitle
            """);
    }

    // --- Task list edge cases ---

    [Fact]
    public void TaskList_InOrderedList()
    {
        AssertHtmlToMarkdown(
            """
            <ol>
                <li><input type="checkbox" disabled> Task A</li>
                <li><input type="checkbox" checked disabled> Task B</li>
            </ol>
            """,
            """
            1. [ ] Task A
            2. [x] Task B
            """);
    }

    [Fact]
    public void TaskList_NestedWithRegularItems()
    {
        AssertHtmlToMarkdown(
            """
            <ul>
                <li><input type="checkbox" disabled> Parent task
                    <ul>
                        <li><input type="checkbox" checked disabled> Sub-task done</li>
                        <li>Not a task</li>
                    </ul>
                </li>
            </ul>
            """,
            """
            - [ ] Parent task
              - [x] Sub-task done
              - Not a task
            """);
    }

    // --- Mixed content that interleaves inline and block ---

    [Fact]
    public void Paragraph_ContainingOnlyImage()
    {
        AssertHtmlToMarkdown(
            """<p><img src="img.png" alt="alt"></p>""",
            "![alt](img.png)");
    }

    [Fact]
    public void Paragraph_ContainingOnlyLink()
    {
        AssertHtmlToMarkdown(
            """<p><a href="url">text</a></p>""",
            "[text](url)");
    }

    [Fact]
    public void Div_ContainingMultipleBlocks()
    {
        AssertHtmlToMarkdown(
            """
            <div>
                <h2>Title</h2>
                <p>Paragraph</p>
                <ul>
                    <li>Item</li>
                </ul>
            </div>
            """,
            """
            ## Title

            Paragraph

            - Item
            """);
    }

    // --- Pre/code variations ---

    [Fact]
    public void Pre_WithLanguageOnPreTag()
    {
        AssertHtmlToMarkdown(
            """<pre class="language-ruby"><code>puts 'hello'</code></pre>""",
            """
            ```ruby
            puts 'hello'
            ```
            """);
    }

    [Fact]
    public void Pre_WithDataLanguageAttribute()
    {
        AssertHtmlToMarkdown(
            """<pre><code data-language="go">fmt.Println("hi")</code></pre>""",
            """
            ```go
            fmt.Println("hi")
            ```
            """);
    }

    [Fact]
    public void Code_InsidePreWithExtraElements()
    {
        AssertHtmlToMarkdown(
            """<pre><code><span class="keyword">var</span> x = <span class="number">1</span>;</code></pre>""",
            """
            ```
            var x = 1;
            ```
            """);
    }

    [Fact]
    public void CodeBlock_ContainsHtmlTags()
    {
        AssertHtmlToMarkdown(
            "<pre><code>&lt;div class=&quot;foo&quot;&gt;bar&lt;/div&gt;</code></pre>",
            """
            ```
            <div class="foo">bar</div>
            ```
            """);
    }
}
