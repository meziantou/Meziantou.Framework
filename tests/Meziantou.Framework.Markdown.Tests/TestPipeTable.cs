using System.Diagnostics;
using System.Text;

using Meziantou.Framework.Markdown;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public sealed class TestPipeTable
{
    [Theory]
    [InlineData("| S | T |\r\n|---|---| \r\n| G | H |")]
    [InlineData("| S | T |\r\n|---|---|\t\r\n| G | H |")]
    [InlineData("| S | T |\r\n|---|---|\f\r\n| G | H |")]
    [InlineData("| S | \r\n|---|\r\n| G |\r\n\r\n| D | D |\r\n| ---| ---| \r\n| V | V |", 2)]
    [InlineData("a\r| S | T |\r|---|---|")]
    [InlineData("a\n| S | T |\r|---|---|")]
    [InlineData("a\r\n| S | T |\r|---|---|")]
    public void TestTableBug(string markdown, int tableCount = 1)
    {
        MarkdownDocument document =
            MarkdownConverter.Parse(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        Table[] tables = document.Descendants().OfType<Table>().ToArray();

        Assert.HasCount(tableCount, tables);
    }

    [Theory]
    [InlineData("A | B\r\n---|---", new[] {50.0f, 50.0f})]
    [InlineData("A | B\r\n-|---", new[] {25.0f, 75.0f})]
    [InlineData("A | B\r\n-|---\r\nA | B\r\n---|---", new[] {25.0f, 75.0f})]
    [InlineData("A | B\r\n---|---|---", new[] {33.33f, 33.33f, 33.33f})]
    [InlineData("A | B\r\n---|---|---|", new[] {33.33f, 33.33f, 33.33f})]
    [InlineData("| A | B | C |\r\n| |---|---|", new[] {0.0f, 50.0f, 50.0f})]
    [InlineData("| A | B | C |\r\n||---|---|", new[] {0.0f, 50.0f, 50.0f})]
    [InlineData("| A | B | C |\r\n|---|\t|---|", new[] {50.0f, 0.0f, 50.0f})]
    [InlineData("| A | B | C |\r\n|---||---|", new[] {50.0f, 0.0f, 50.0f})]
    [InlineData("| A | B | C |\r\n|---|---| |", new[] {50.0f, 50.0f, 0.0f})]
    [InlineData("| A | B | C |\r\n|---|---||", new[] {50.0f, 50.0f, 0.0f})]
    [InlineData("A | B | C\r\n---||------\r\n1 | 2 | 3", new[] {33.33f, 0.0f, 66.67f})]
    [InlineData("| A | B | C |\r\n||---||\r\n| 1 | 2 | 3 |", new[] {0.0f, 100.0f, 0.0f})]
    public void TestColumnWidthByHeaderLines(string markdown, float[] expectedWidth)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables(new PipeTableOptions() {InferColumnWidthsFromSeparator = true})
            .Build();
        var document = MarkdownConverter.Parse(markdown, pipeline);
        var table = document.Descendants().OfType<Table>().FirstOrDefault();
        Assert.NotNull(table);
        var actualWidths = table.ColumnDefinitions.Select(x => x.Width).ToList();
        Assert.HasCount(expectedWidth.Length, actualWidths);
        for (int i = 0; i < expectedWidth.Length; i++)
        {
            Assert.Equal(expectedWidth[i], actualWidths[i], 0.01f);
        }
    }

    public static TheoryData<string, bool, bool> InvalidSeparatorCases()
    {
        var result = new TheoryData<string, bool, bool>();
        foreach (var separator in new[] { "|||", "| | | |", "|\t|\t|\t|", "| : | : | : |", "|---|text||", "|---|:||" })
        {
            foreach (var inferColumnWidths in new[] { false, true })
            {
                foreach (var headerOnly in new[] { false, true })
                {
                    result.Add(separator, inferColumnWidths, headerOnly);
                }
            }
        }

        return result;
    }

    [Theory]
    [MemberData(nameof(InvalidSeparatorCases))]
    public void InvalidSeparatorRemainsParagraph(string separator, bool inferColumnWidths, bool headerOnly)
    {
        var markdown = "| A | B | C |\n" + separator;
        if (!headerOnly)
        {
            markdown += "\n| 1 | 2 | 3 |";
        }

        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables(new PipeTableOptions { InferColumnWidthsFromSeparator = inferColumnWidths })
            .Build();

        Assert.Equal("<p>" + markdown + "</p>\n", MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Theory]
    [InlineData("a|b\n-|- *emph*", "<p>a|b\n-|- <em>emph</em></p>\n")]
    [InlineData("a | b\n--- | ---  [link](/u)", "<p>a | b\n--- | ---  <a href=\"/u\">link</a></p>\n")]
    [InlineData("a | b\n`x` - | -", "<p>a | b\n<code>x</code> - | -</p>\n")]
    [InlineData("a | b\n**bold** - | -\nc | d", "<p>a | b\n<strong>bold</strong> - | -\nc | d</p>\n")]
    [InlineData("|\n`a`-|", "<p>|\n<code>a</code>-|</p>\n")]
    [InlineData("|\n&ap;-|", "<p>|\n≈-|</p>\n")]
    public void SeparatorRowWithOtherInlinesRemainsParagraph(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UsePipeTables().Build()));
        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()));
    }

    [Theory]
    [InlineData("a | b\n:-- | --:\nc | d")]
    [InlineData("| a | b |\n| :-: | - |\n| c | d |")]
    [InlineData("a | b\n:-\t| --  \nc | d")]
    public void SeparatorRowWithOnlyDashesColonsAndWhitespaceIsATable(string markdown)
    {
        Assert.Contains("<table>", MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UsePipeTables().Build()));
    }

    [Fact]
    public void TestColumnWidthIsNotSetWithoutConfigurationFlag()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables(new PipeTableOptions() {InferColumnWidthsFromSeparator = false})
            .Build();
        var document = MarkdownConverter.Parse("| A | B | C |\r\n|---|---|---|", pipeline);
        var table = document.Descendants().OfType<Table>().FirstOrDefault();
        Assert.NotNull(table);
        foreach (var column in table.ColumnDefinitions)
        {
            Assert.Equal(0, column.Width);
        }
    }

    [Fact]
    public void TableWithUnbalancedCodeSpanParsesWithoutDepthLimitError()
    {
        var markdown = """
| Count | A | B | C | D | E |
|-------|---|---|---|---|---|
|     0 | B | C | D | E | F |
|     1 | B | `C | D | E | F |
|     2 | B | `C | D | E | F |
|     3 | B | C | D | E | F |
|     4 | B | C | D | E | F |
|     5 | B | C | D | E | F |
|     6 | B | C | D | E | F |
|     7 | B | C | D | E | F |
|     8 | B | C | D | E | F |
|     9 | B | C | D | E | F |
|    10 | B | C | D | E | F |
|    11 | B | C | D | E | F |
|    12 | B | C | D | E | F |
|    13 | B | C | D | E | F |
|    14 | B | C | D | E | F |
|    15 | B | C | D | E | F |
|    16 | B | C | D | E | F |
|    17 | B | C | D | E | F |
|    18 | B | C | D | E | F |
|    19 | B | C | D | E | F |
""";

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        MarkdownDocument document = null!;
        Assert.DoesNotThrow(() => document = MarkdownConverter.Parse(markdown, pipeline));

        var tables = document.Descendants().OfType<Table>().ToArray();
        Assert.HasCount(1, tables);

        string html = string.Empty;
        Assert.DoesNotThrow(() => html = MarkdownConverter.ToHtml(markdown, pipeline));
        Assert.Contains("<table", html);
        Assert.Contains("<td>`C</td>", html);
    }

    [Fact]
    public void CodeInlineWithPipeDelimitersRemainsCodeInline()
    {
        var markdown = "`|| hidden text ||`";

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var document = MarkdownConverter.Parse(markdown, pipeline);

        var codeInline = document.Descendants().OfType<CodeInline>().SingleOrDefault();
        Assert.NotNull(codeInline);
        Assert.Equal("|| hidden text ||", codeInline!.Content);
        Assert.Equal("<p><code>|| hidden text ||</code></p>\n", document.ToHtml());
    }

    [Fact]
    public void MultiLineCodeInlineWithPipeDelimitersRendersAsCode()
    {
        string markdown =
            """
            `
            || hidden text ||
            `
            """.ReplaceLineEndings("\n");

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Equal("<p><code>|| hidden text ||</code></p>\n", html);
    }

    [Fact]
    public void TableCellWithCodeInlineRendersCorrectly()
    {
        var markdown =
            """
            | Count | A | B | C | D | E |
            |-------|---|---|---|---|---|
            |     0 | B | C | D | E | F |
            |     1 | B | `Code block` | D | E | F |
            |     2 | B | C | D | E | F |
            """;

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Contains("<td><code>Code block</code></td>", html);
    }

    [Fact]
    public void BoldTableCellWithUnmatchedSubscriptDelimiterDoesNotAddCells()
    {
        var markdown =
            """
            | Component | Per query | Per 1,000 queries |
            |-----------|-----------|-------------------|
            | Embedding | ~$0.00001 | ~$0.01 |
            | LLM | ~$0.0015 | ~$1.50 |
            | **Total** | **~$0.0015** | **~$1.50** |
            """;

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var document = MarkdownConverter.Parse(markdown, pipeline);
        var table = document.Descendants().OfType<Table>().Single();
        var rows = table.OfType<TableRow>().ToArray();

        Assert.HasCount(4, rows);
        Assert.All(rows, row => Assert.HasCount(3, row));

        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Contains("<td><strong>~$0.0015</strong></td>", html);
        Assert.Contains("<td><strong>~$1.50</strong></td>", html);
    }

    [Fact]
    public void CodeInlineWithIndentedContentPreservesWhitespace()
    {
        var markdown = "`\n   foo\n`";

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var document = MarkdownConverter.Parse(markdown, pipeline);
        var codeInline = document.Descendants().OfType<CodeInline>().Single();

        Assert.Equal("foo", codeInline.Content);
        Assert.Equal("<p><code>foo</code></p>\n", MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Fact]
    public void TableWithIndentedPipeAfterCodeInlineParsesCorrectly()
    {
        var markdown =
            """
            `
            	|| hidden text ||
            `

              | Count | Value |
              |-------|-------|
              | 0     | B |

            """.ReplaceLineEndings("\n");

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        Assert.Contains("<p><code>|| hidden text ||</code></p>", html);
        Assert.Contains("<table", html);
        Assert.Contains("<td>B</td>", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortRowsAreNotCompletedBeyondTheCellBudget(bool useHeaderForColumnCount)
    {
        const int Count = 2000;
        var markdown = string.Concat(Enumerable.Repeat("a|", Count)) + "\n" + string.Concat(Enumerable.Repeat("-|", Count)) + "\n" + string.Concat(Enumerable.Repeat("a|\n", Count));
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions { UseHeaderForColumnCount = useHeaderForColumnCount }).Build();

        var table = Assert.Single(MarkdownConverter.Parse(markdown, pipeline).Descendants<Table>());

        Assert.HasCount(Count + 1, table);
        Assert.HasCount(Count, (TableRow)table[0]);
        Assert.HasCount(1, (TableRow)table[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortRowsAreNotCompletedBeyondTheDocumentBudget(bool useGfmRules)
    {
        // Each table needs 519,480 empty cells, just under the budget, so the budget must apply to the whole document
        var builder = new StringBuilder();
        for (var i = 0; i < 10; i++)
        {
            builder.Append(string.Concat(Enumerable.Repeat("|a", 1000))).Append("|\n");
            builder.Append(string.Concat(Enumerable.Repeat("|-", 1000))).Append("|\n");
            builder.Append(string.Concat(Enumerable.Repeat(useGfmRules ? "x\n" : "|x\n", 520)));
            builder.Append('\n');
        }

        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions { UseGfmRules = useGfmRules }).Build();
        var document = MarkdownConverter.Parse(builder.ToString(), pipeline);

        var cellCount = document.Descendants<TableCell>().Count();
        Assert.InRange(cellCount, 524_288, 600_000);
    }

    [Fact]
    public void GfmTablesAfterTheCellBudgetKeepTheirRows()
    {
        // The first table needs 1,099,000 empty cells, more than the budget of the whole document: its first rows are
        // completed, the others are kept as they are, and the budget left completes the short row of the second table
        var markdown = string.Concat(Enumerable.Repeat("|a", 1100)) + "|\n" + string.Concat(Enumerable.Repeat("|-", 1100)) + "|\n" + string.Concat(Enumerable.Repeat("x\n", 1000)) + "\n| x | y |\n|---|---|\n| 1 | 2 |\n| 3 |\n";
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions { UseGfmRules = true }).Build();

        var tables = MarkdownConverter.Parse(markdown, pipeline).Descendants<Table>().ToList();

        Assert.HasCount(2, tables);
        Assert.HasCount(1001, tables[0]);
        Assert.HasCount(1100, (TableRow)tables[0][1]);
        Assert.HasCount(1, (TableRow)tables[0][1000]);
        Assert.HasCount(3, tables[1]);
        Assert.HasCount(2, (TableRow)tables[1][1]);
        Assert.HasCount(2, (TableRow)tables[1][2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortRowsAreCompletedWithinTheCellBudget(bool useHeaderForColumnCount)
    {
        const int Count = 500;
        var markdown = string.Concat(Enumerable.Repeat("a|", Count)) + "\n" + string.Concat(Enumerable.Repeat("-|", Count)) + "\n" + string.Concat(Enumerable.Repeat("a|\n", Count));
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions { UseHeaderForColumnCount = useHeaderForColumnCount }).Build();

        var table = Assert.Single(MarkdownConverter.Parse(markdown, pipeline).Descendants<Table>());

        Assert.All(table, row => Assert.HasCount(Count, (TableRow)row));
    }

    [Fact]
    public void ExtraCellsAreRemovedWhenTheHeaderGivesTheColumnCount()
    {
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions { UseHeaderForColumnCount = true }).Build();

        var table = Assert.Single(MarkdownConverter.Parse("a|b\n-|-\n1|2|3|4|5", pipeline).Descendants<Table>());

        Assert.Equal(new[] { 2, 2 }, table.Select(row => ((TableRow)row).Count).ToArray());
        Assert.Equal("1 2", string.Join(' ', ((TableRow)table[1]).Select(cell => ((ParagraphBlock)((TableCell)cell)[0]).Inline!.FirstChild!.ToString())));
    }

    [Fact]
    public void NormalizeUsingMaxWidth()
    {
        var table = CreateTable(2, 4, 1);

        table.NormalizeUsingMaxWidth();

        Assert.Equal(new[] { 4, 4, 4 }, table.Select(row => ((TableRow)row).Count).ToArray());
    }

    [Fact]
    public void NormalizeUsingHeaderRow()
    {
        var table = CreateTable(2, 5, 1);

        table.NormalizeUsingHeaderRow();

        Assert.Equal(new[] { 2, 2, 2 }, table.Select(row => ((TableRow)row).Count).ToArray());
        new Table().NormalizeUsingHeaderRow();
    }

    private static Table CreateTable(params int[] cellCounts)
    {
        var table = new Table();
        foreach (var cellCount in cellCounts)
        {
            var row = new TableRow();
            for (var i = 0; i < cellCount; i++)
            {
                row.Add(new TableCell());
            }

            table.Add(row);
        }

        return table;
    }

    [Fact]
    public void LargeTableWithEmphasisDoesNotExceedTheDepthLimit()
    {
        // The whole table is a single paragraph: its emphasis delimiters nest across all the rows until they are resolved
        const int RowCount = 3000;
        var markdown = "| a | b |\n|---|---|\n" + string.Concat(Enumerable.Repeat("| *x* | **y** |\n", RowCount));
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

        var html = MarkdownConverter.ToHtml(markdown, pipeline);

        var expected = "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n"
            + string.Concat(Enumerable.Repeat("<tr>\n<td><em>x</em></td>\n<td><strong>y</strong></td>\n</tr>\n", RowCount))
            + "</tbody>\n</table>\n";
        Assert.Equal(expected, html);
    }

    // Timed: tests running at the same time would slow it down and make the time budget flaky
    [Fact(DisableParallelization = true)]
    public void LargeTableWithEmphasisAndLinksIsParsedInLinearTime()
    {
        const int RowCount = 8000;
        var markdown = "| a | b |\n|---|---|\n" + string.Concat(Enumerable.Repeat("| *x* | [l](u) |\n", RowCount));
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        var expected = "<table>\n<thead>\n<tr>\n<th>a</th>\n<th>b</th>\n</tr>\n</thead>\n<tbody>\n"
            + string.Concat(Enumerable.Repeat("<tr>\n<td><em>x</em></td>\n<td><a href=\"u\">l</a></td>\n</tr>\n", RowCount))
            + "</tbody>\n</table>\n";
        Assert.Equal(expected, html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }

    // Timed: tests running at the same time would slow it down and make the time budget flaky
    [Fact(DisableParallelization = true)]
    public void LargeTableWithUnclosedCodeSpansIsParsedInLinearTime()
    {
        // An unclosed code span followed by a line starting with a pipe looks for a pipe delimiter among its parents
        const int RowCount = 8000;
        var markdown = "| a |\n|---|\n" + string.Concat(Enumerable.Repeat("| *x* `c |\n", RowCount));
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        var expected = "<table>\n<thead>\n<tr>\n<th>a</th>\n</tr>\n</thead>\n<tbody>\n"
            + string.Concat(Enumerable.Repeat("<tr>\n<td><em>x</em> `c</td>\n</tr>\n", RowCount))
            + "</tbody>\n</table>\n";
        Assert.Equal(expected, html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }

    // Timed: tests running at the same time would slow it down and make the time budget flaky
    [Fact(DisableParallelization = true)]
    public void ManyColonsBeforePipesOnALineAreParsedInLinearTime()
    {
        // Each ":|" checked whether its whole line is a header separator line
        var markdown = string.Concat(Enumerable.Repeat(":| ", 50_000));
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        Assert.Equal("<p>" + markdown.TrimEnd() + "</p>\n", html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {stopwatch.Elapsed}");
    }
}
