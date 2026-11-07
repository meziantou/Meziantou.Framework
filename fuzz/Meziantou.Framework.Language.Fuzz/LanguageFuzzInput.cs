using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>Reads the options the targets share from the option bytes, and the corpora the seeds come from.</summary>
internal static class LanguageFuzzInput
{
    /// <summary>The nesting limits a depth byte picks from: the default, and small ones that the fuzzer reaches easily.</summary>
    private static readonly int[] Depths = [128, 1, 2, 3, 5, 8, 32, 128];

    /// <summary>Picks a nesting limit (at least one, as the options of the parsers require) from a byte.</summary>
    public static int ReadDepth(byte value) => FuzzInput.Pick(Depths, value);

    /// <summary>Reads the records of a corpus that separates them with lines holding only <c>%%</c> (the regex corpora of the unit tests).</summary>
    public static IEnumerable<string> ReadPercentRecords(string resourceName)
    {
        var text = FuzzResources.ReadText(typeof(LanguageFuzzInput).Assembly, resourceName);
        var builder = new StringBuilder();
        var started = false;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (line == "%%")
            {
                if (started)
                {
                    yield return builder.ToString();
                    builder.Clear();
                    started = false;
                }

                continue;
            }

            if (line is ['#', ..] && !started)
                continue;

            if (started)
            {
                builder.Append('\n');
            }

            builder.Append(line);
            started = true;
        }

        if (started)
        {
            yield return builder.ToString();
        }
    }

    /// <summary>Reads the records of a JSON Lines corpus.</summary>
    public static IEnumerable<System.Text.Json.JsonElement> ReadJsonLines(string resourceName)
    {
        var text = FuzzResources.ReadText(typeof(LanguageFuzzInput).Assembly, resourceName);
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length == 0)
                continue;

            using var document = System.Text.Json.JsonDocument.Parse(line);
            yield return document.RootElement.Clone();
        }
    }
}
