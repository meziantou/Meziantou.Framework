namespace Meziantou.Framework.SyntaxHighlighting.Benchmarks;

internal static class Samples
{
    public static string Get(string language)
    {
        if (language is "html")
        {
            // A page that embeds CSS and JavaScript, so the sub-language runs are measured too.
            var css = Read("css");
            var script = FirstLines(Read("javascript"), 120);
            var markup = new StringBuilder();
            markup.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>Log viewer</title>\n<style>\n").Append(css).Append("</style>\n</head>\n<body>\n");
            for (var i = 0; i < 40; i++)
            {
                markup.Append("<div class=\"log-entry\" data-index=\"").Append(i).Append("\">\n  <span class=\"timestamp\">2026-11-25T10:00:").Append(i % 60).Append("Z</span>\n  <a href=\"/entries/").Append(i).Append("?filter=all&amp;sort=desc\" title='Entry ").Append(i).Append("'>Entry &lt;").Append(i).Append("&gt;</a>\n  <!-- entry ").Append(i).Append(" -->\n</div>\n");
            }

            markup.Append("<script>\n").Append(script).Append("</script>\n</body>\n</html>\n");
            return markup.ToString();
        }

        return Read(language);
    }

    public static string FirstLines(string text, int count)
    {
        var index = 0;
        for (var i = 0; i < count && index >= 0; i++)
        {
            index = text.IndexOf('\n', index + 1, StringComparison.Ordinal);
        }

        return index < 0 ? text : text[..(index + 1)];
    }

    private static string Read(string name)
    {
        using var stream = typeof(Samples).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Sample '{name}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
