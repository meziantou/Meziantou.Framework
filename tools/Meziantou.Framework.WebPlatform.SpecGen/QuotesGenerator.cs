using System.Text.Json;

namespace SpecGen;

/// <summary>
/// Generates the quotation marks of the languages from the delimiters of the Unicode CLDR
/// (https://cldr.unicode.org/, <c>tests/Meziantou.Framework.WebPlatform.SpecData/cldr</c>), used by <c>quotes: auto</c>
/// (https://drafts.csswg.org/css-content-3/#valdef-quotes-auto). A locale is listed when its quotation marks differ from the
/// ones of the locale without its last subtag (the root locale has the English quotation marks).
/// </summary>
internal static class QuotesGenerator
{
    private const string DefaultQuotes = "“”‘’";

    public static void Generate(GeneratorContext context)
    {
        var directory = context.SpecData("cldr", "cldr-json", "cldr-misc-full", "main");
        var quotes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var localeDirectory in Directory.EnumerateDirectories(directory))
        {
            var file = Path.Combine(localeDirectory, "delimiters.json");
            if (!File.Exists(file))
                continue;

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var locale in document.RootElement.GetProperty("main").EnumerateObject())
            {
                if (!locale.Value.TryGetProperty("delimiters", out var delimiters))
                    continue;

                var value = string.Join('\0', Get("quotationStart"), Get("quotationEnd"), Get("alternateQuotationStart"), Get("alternateQuotationEnd"));
                quotes[locale.Name.ToLowerInvariant()] = value;

                string Get(string name) => delimiters.GetProperty(name).GetString() ?? "";
            }
        }

        var defaultValue = string.Join('\0', DefaultQuotes.Select(c => c.ToString()));
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/cldr/cldr-json/cldr-misc-full/main/*/delimiters.json");
        writer.Line("using System.Collections.Frozen;").Line();
        writer.Line("namespace Meziantou.Framework.WebPlatform.Css.Layout;").Line();
        using (writer.Block("internal static partial class LanguageQuotes"))
        {
            writer.Line("/// <summary>");
            writer.Line("/// The quotation marks of the locales (in lowercase) whose marks differ from the ones of the locale without its last");
            writer.Line("/// subtag: the opening and closing marks, then the ones of the nested quotations.");
            writer.Line("/// </summary>");
            writer.Line("private static readonly FrozenDictionary<string, string[]> Data = new Dictionary<string, string[]>(StringComparer.Ordinal)");
            writer.Line("{");
            foreach (var (locale, value) in quotes)
            {
                // The marks of the parent: the nearest locale without the last subtags, else the default marks
                var parent = defaultValue;
                for (var index = locale.LastIndexOf('-', StringComparison.Ordinal); index > 0; index = locale.LastIndexOf('-', index - 1, StringComparison.Ordinal))
                {
                    if (quotes.TryGetValue(locale[..index], out var parentValue))
                    {
                        parent = parentValue;
                        break;
                    }
                }

                if (value == parent || locale == "und")
                    continue;

                writer.Line($"    [{CodeWriter.Literal(locale)}] = [{string.Join(", ", value.Split('\0').Select(CodeWriter.Literal))}],");
            }

            writer.Line("}.ToFrozenDictionary(StringComparer.Ordinal);");
        }

        context.WriteFile(context.Source("Css", "Layout", "BoxTree", "LanguageQuotes.g.cs"), writer.ToString());
    }
}
