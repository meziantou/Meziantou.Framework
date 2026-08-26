namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>Reads the embedded pattern corpora.</summary>
/// <remarks>
/// Records are separated by a line containing only <c>%%</c> rather than one pattern per line, because a pattern in
/// extended mode contains real line breaks and any escaping convention would collide with the backslashes patterns are
/// made of.
/// </remarks>
internal static class RegexCorpus
{
    public static IEnumerable<string> Read(string name)
    {
        using var stream = typeof(RegexCorpus).Assembly.GetManifestResourceStream($"Meziantou.Framework.Language.Regex.Tests.Corpus.{name}")
            ?? throw new InvalidOperationException($"The corpus '{name}' is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);

        var builder = new StringBuilder();
        var started = false;
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
}
