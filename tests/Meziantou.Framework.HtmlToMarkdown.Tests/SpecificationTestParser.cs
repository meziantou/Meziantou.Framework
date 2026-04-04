namespace Meziantou.Framework.HtmlToMarkdownTests;

internal static class SpecificationTestParser
{
    public static List<MarkdigTestCase> Parse(string content, string fileName)
    {
        var testCases = new List<MarkdigTestCase>();
        var lines = content.Split('\n');
        var exampleNumber = 0;
        var i = 0;

        while (i < lines.Length)
        {
            // Look for the start of an example block
            if (IsExampleStart(lines[i]))
            {
                exampleNumber++;
                i++;

                var markdownLines = new List<string>();
                var htmlLines = new List<string>();
                var inHtml = false;

                while (i < lines.Length && !IsExampleEnd(lines[i]))
                {
                    var line = lines[i];

                    // The separator between markdown and html is a single "."
                    if (!inHtml && line.TrimEnd('\r') == ".")
                    {
                        inHtml = true;
                        i++;
                        continue;
                    }

                    if (inHtml)
                    {
                        htmlLines.Add(line);
                    }
                    else
                    {
                        markdownLines.Add(line);
                    }

                    i++;
                }

                var markdown = string.Join('\n', markdownLines);
                var html = string.Join('\n', htmlLines);

                // Trim trailing \r from each value (Windows line endings)
                markdown = markdown.TrimEnd('\r', '\n');
                html = html.TrimEnd('\r', '\n');

                if (!string.IsNullOrEmpty(html))
                {
                    testCases.Add(new MarkdigTestCase
                    {
                        Markdown = markdown,
                        Html = html,
                        Example = exampleNumber,
                        FileName = fileName,
                    });
                }
            }

            i++;
        }

        return testCases;
    }

    private static bool IsExampleStart(string line)
    {
        var trimmed = line.TrimEnd('\r');
        return trimmed.StartsWith("````", StringComparison.Ordinal)
            && trimmed.EndsWith("example", StringComparison.Ordinal);
    }

    private static bool IsExampleEnd(string line)
    {
        var trimmed = line.TrimEnd('\r');
        return trimmed.StartsWith("````", StringComparison.Ordinal)
            && !trimmed.EndsWith("example", StringComparison.Ordinal);
    }
}
