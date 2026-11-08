using System.Collections.Frozen;

namespace Meziantou.Framework.Barcodes.Internal.Gs1;

// The GS1 Barcode Syntax Dictionary (https://github.com/gs1/gs1-syntax-dictionary, Apache License 2.0, see
// THIRD-PARTY-NOTICES.md), embedded as published. Each entry describes the components of the data of an application
// identifier, the linters that check their content, and the identifiers it requires or excludes.
internal static class Gs1SyntaxDictionary
{
    internal const string ResourceName = "Meziantou.Framework.Barcodes.gs1-syntax-dictionary.txt";

    private static readonly Lazy<FrozenDictionary<string, Gs1DictionaryEntry>> LazyEntries = new(Load);

    public static FrozenDictionary<string, Gs1DictionaryEntry> Entries => LazyEntries.Value;

    public static Gs1DictionaryEntry? Find(string applicationIdentifier) => Entries.GetValueOrDefault(applicationIdentifier);

    private static FrozenDictionary<string, Gs1DictionaryEntry> Load()
    {
        using var stream = typeof(Gs1SyntaxDictionary).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException($"The resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    internal static FrozenDictionary<string, Gs1DictionaryEntry> Parse(string content)
    {
        var result = new Dictionary<string, Gs1DictionaryEntry>(StringComparer.Ordinal);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
                continue;

            var title = "";
            var titleIndex = line.IndexOf('#', StringComparison.Ordinal);
            if (titleIndex >= 0)
            {
                title = line[(titleIndex + 1)..].Trim();
                line = line[..titleIndex];
            }

            var tokens = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            var index = 1;
            var predefinedLength = false;
            if (index < tokens.Length && IsFlags(tokens[index]))
            {
                predefinedLength = tokens[index].Contains('*', StringComparison.Ordinal);
                index++;
            }

            var components = new List<Gs1DictionaryComponent>();
            while (index < tokens.Length && TryParseComponent(tokens[index], out var component))
            {
                components.Add(component);
                index++;
            }

            if (components.Count == 0)
                throw new FormatException($"The entry '{rawLine}' of the GS1 syntax dictionary has no component.");

            var requirements = new List<string[][]>();
            var exclusions = new List<string>();
            for (; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (token.StartsWith("req=", StringComparison.Ordinal))
                {
                    requirements.Add([.. token[4..].Split(',').Select(alternative => alternative.Split('+'))]);
                }
                else if (token.StartsWith("ex=", StringComparison.Ordinal))
                {
                    exclusions.AddRange(token[3..].Split(','));
                }
            }

            foreach (var applicationIdentifier in ExpandRange(tokens[0]))
            {
                result.Add(applicationIdentifier, new Gs1DictionaryEntry(applicationIdentifier, title, predefinedLength, [.. components], [.. requirements], [.. exclusions]));
            }
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static bool IsFlags(string token)
    {
        foreach (var c in token)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '[' or ']')
                return false;
        }

        return true;
    }

    // "N13", "X..20", "[N3]" or "[X..17]", followed by linters: "N13,csum,gcppos1"
    private static bool TryParseComponent(string token, out Gs1DictionaryComponent component)
    {
        component = default!;
        var parts = token.Split(',');
        var type = parts[0];
        var optional = type.StartsWith('[', StringComparison.Ordinal) && type.EndsWith(']', StringComparison.Ordinal);
        if (optional)
        {
            type = type[1..^1];
        }

        if (type.Length < 2 || type[0] is not ('N' or 'X' or 'Y' or 'Z'))
            return false;

        int minLength, maxLength;
        if (type.AsSpan(1).StartsWith(".."))
        {
            if (!int.TryParse(type.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out maxLength))
                return false;

            minLength = 1;
        }
        else
        {
            if (!int.TryParse(type.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out maxLength))
                return false;

            minLength = maxLength;
        }

        var linters = parts[1..];
        foreach (var linter in linters)
        {
            if (!Gs1Linters.IsKnown(linter))
                throw new FormatException($"The linter '{linter}' of the GS1 syntax dictionary is not supported.");
        }

        component = new Gs1DictionaryComponent(type[0], minLength, maxLength, optional, linters);
        return true;
    }

    private static IEnumerable<string> ExpandRange(string token)
    {
        var dash = token.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            yield return token;
            yield break;
        }

        var first = token[..dash];
        var last = token[(dash + 1)..];
        var start = int.Parse(first, NumberStyles.None, CultureInfo.InvariantCulture);
        var end = int.Parse(last, NumberStyles.None, CultureInfo.InvariantCulture);
        for (var value = start; value <= end; value++)
        {
            yield return value.ToString(CultureInfo.InvariantCulture).PadLeft(first.Length, '0');
        }
    }

    // A pattern of application identifiers, where "n" stands for any digit: "31nn" matches 3100 to 3199
    public static bool MatchesPattern(string applicationIdentifier, string pattern)
    {
        if (applicationIdentifier.Length != pattern.Length)
            return false;

        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == 'n' ? !char.IsAsciiDigit(applicationIdentifier[i]) : pattern[i] != applicationIdentifier[i])
                return false;
        }

        return true;
    }
}
