using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Ini;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// INI documents in every dialect the options describe: the shared invariants, the grouping of the properties under
/// their section, the lookups against a scan of the entries, and <see cref="IniDocumentSyntax.SetValue"/>, whose value
/// must read back from the edited document and from its text parsed again, without changing the other values.
/// </summary>
internal static class IniFuzzTargets
{
    /// <summary>Separates the document from the value <see cref="IniDocumentSyntax.SetValue"/> writes (an invalid UTF-8 byte, so it never ends a character).</summary>
    private const byte ValueSeparator = 0xFF;

    private const int LookupSampleCount = 8;

    private static readonly string[] Tokens =
    [
        "[", "]", "[section]", "[remote \"origin\"]", "=", ":", " = ", ";", "#", " ;", " #", "\"", "'", "\"a;b\"", "'x'", "\n", "\r\n", "\r", "\t", "    ", " ", "\uFEFF",
        "key", "Key", "KEY", "name[fr]", "my key", "value", "true", "http://host/#top", "\\", "\n    continued", "\n\n", "[]", "[[", "]]",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-ini", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// Byte 0: the options (bits 0-1 inline comments, 2 keys without value, 3 multiline values, 4 no quoted values, 5 report
    /// duplicates, 6 ordinal names). Byte 1: bit 0 sets a value, the other bits pick the property. Then the document, then
    /// 0xFF and the value to set.
    /// </summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var edit = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(FuzzInput.ReadUntil(ref data, ValueSeparator));
        var value = FuzzText.Decode(data);
        var options = new IniParseOptions
        {
            InlineComments = (IniInlineCommentMode)((flags & 3) % 3),
            AllowKeysWithoutValue = (flags & 4) != 0,
            AllowMultilineValues = (flags & 8) != 0,
            AllowQuotedValues = (flags & 16) == 0,
            ReportDuplicates = (flags & 32) != 0,
            NameComparer = (flags & 64) == 0 ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal,
        };

        var tree = IniSyntaxTree.ParseText(text, options);
        var shape = SyntaxTreeInvariants.Check(tree, text, "INI", diagnosticsOnNodes: true, diagnosticsInSourceOrder: true);
        var root = tree.GetRoot();
        FuzzAssert.True(root.Options == options, "The document does not keep the options it was parsed with");

        var layout = CheckLayout(root);
        var pairs = SampleLookups(layout);
        foreach (var (section, key) in pairs)
        {
            FuzzAssert.Equal(ExpectedValue(layout, options.NameComparer, section, key), root.GetValue(section, key), $"GetValue({FuzzAssert.Format(section)}, {FuzzAssert.Format(key)}) does not return the value of the last matching property");
        }

        var walker = new CountingWalker();
        try
        {
            walker.Visit(root);
            SyntaxTreeInvariants.CheckWalker(shape, walker.TokenCount, walker.TriviaCount);
        }
        catch (InsufficientExecutionStackException)
        {
            // Documented by the walker for a tree nested too deeply to walk
        }

        if ((edit & 1) != 0)
        {
            SetValue(root, options, layout, pairs, edit >> 1, value);
        }
    }

    /// <summary>Checks that the properties are grouped under the section header before them, and returns that grouping.</summary>
    private static Layout CheckLayout(IniDocumentSyntax root)
    {
        var global = new List<IniPropertySyntax>();
        var sections = new List<(IniSectionSyntax Section, List<IniPropertySyntax> Properties)>();
        foreach (var entry in root.Entries)
        {
            switch (entry)
            {
                case IniSectionSyntax section:
                    _ = section.Name;
                    sections.Add((section, []));
                    break;

                case IniPropertySyntax property:
                    _ = property.Key;
                    _ = property.Value;
                    (sections.Count == 0 ? global : sections[^1].Properties).Add(property);
                    break;
            }
        }

        FuzzAssert.True(root.GlobalProperties.SequenceEqual(global, ReferenceEqualityComparer.Instance), "GlobalProperties are not the properties before the first section header");
        FuzzAssert.True(root.Sections.SequenceEqual(sections.Select(item => item.Section), ReferenceEqualityComparer.Instance), "Sections are not the section headers of the entries");
        foreach (var (section, properties) in sections)
        {
            FuzzAssert.True(section.Properties.SequenceEqual(properties, ReferenceEqualityComparer.Instance), $"The properties of the section {FuzzAssert.Format(section.Name)} are not the properties up to the next header");
        }

        return new Layout(global, sections);
    }

    /// <summary>Picks a few (section, key) pairs of the document to look up, and one that it does not have.</summary>
    private static List<(string? Section, string Key)> SampleLookups(Layout layout)
    {
        var all = new List<(string? Section, string Key)>();
        foreach (var property in layout.Global)
        {
            if (!property.KeyToken.IsMissing)
            {
                all.Add((null, property.Key));
            }
        }

        foreach (var (section, properties) in layout.Sections)
        {
            if (section.NameToken.IsMissing)
                continue;

            foreach (var property in properties)
            {
                if (!property.KeyToken.IsMissing)
                {
                    all.Add((section.Name, property.Key));
                }
            }
        }

        var result = new List<(string? Section, string Key)>();
        var step = Math.Max(1, all.Count / LookupSampleCount);
        for (var i = 0; i < all.Count; i += step)
        {
            result.Add(all[i]);
        }

        result.Add((null, "fuzz-missing-key"));
        return result;
    }

    /// <summary>Gets the value of the last property named <paramref name="key"/> of the sections named <paramref name="section"/>, by scanning the entries.</summary>
    private static string? ExpectedValue(Layout layout, StringComparer comparer, string? section, string key)
    {
        IEnumerable<IniPropertySyntax> properties = section is null
            ? layout.Global
            : layout.Sections.Where(item => !item.Section.NameToken.IsMissing && comparer.Equals(item.Section.Name, section)).SelectMany(item => item.Properties);
        return properties.LastOrDefault(property => !property.KeyToken.IsMissing && comparer.Equals(property.Key, key))?.Value;
    }

    private static void SetValue(IniDocumentSyntax root, IniParseOptions options, Layout layout, List<(string? Section, string Key)> pairs, int choice, string value)
    {
        // An existing property (the last pair is the key the document does not have), or a key of a new section
        var (section, key) = choice % (pairs.Count + 1) is var index && index < pairs.Count
            ? pairs[index]
            : (layout.Sections.Count > 0 ? "fuzz-section" : null, "fuzz-key");

        IniDocumentSyntax edited;
        try
        {
            edited = root.SetValue(section, key, value);
        }
        catch (ArgumentException)
        {
            // Documented for a key, section, or value that cannot be written the way the options read it
            return;
        }

        // The lines of a value are read back joined with \n, whichever line break separated them
        var expected = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var setText = $"SetValue({FuzzAssert.Format(section)}, {FuzzAssert.Format(key)}, {FuzzAssert.Format(value)})";
        FuzzAssert.Equal(expected, edited.GetValue(section, key), $"The document returned by {setText} does not have the value");

        var editedText = edited.ToFullString();
        var reparsed = IniSyntaxTree.ParseText(editedText, options).GetRoot();
        FuzzAssert.Equal(expected, reparsed.GetValue(section, key), $"The text written by {setText} does not read back as the value: {FuzzAssert.Format(editedText)}");
        foreach (var (otherSection, otherKey) in pairs)
        {
            if (SameSection(options.NameComparer, section, otherSection) && options.NameComparer.Equals(key, otherKey))
                continue;

            FuzzAssert.Equal(root.GetValue(otherSection, otherKey), reparsed.GetValue(otherSection, otherKey), $"{setText} changes the value of {FuzzAssert.Format(otherSection)}/{FuzzAssert.Format(otherKey)}: {FuzzAssert.Format(editedText)}");
        }

        static bool SameSection(StringComparer comparer, string? left, string? right)
            => left is null ? right is null : right is not null && comparer.Equals(left, right);
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        yield return FuzzSeed.FromText("simple", [0, 0], "; leading comment\n[database]\nserver=localhost\nport: 5432\n\n# another comment\n[features]\nenabled=true\nempty=\n");
        yield return FuzzSeed.FromText("invalid", [0, 0], "[invalid\nkey without separator\n]=value\n");
        yield return Seed("global", 0, 1, "name=value\nother = 1 ; comment\n[s]\na=b\n", "new value");
        yield return FuzzSeed.FromText("quoted", [0, 3], "[remote \"origin\"]\nurl = \"http://host/#top\" ; note\nfetch='+refs/heads/*'\npath=\"a ;b\" c\n");
        yield return FuzzSeed.FromText("inline-anywhere", [2, 5], "password=abc#123\nurl=http://host/#top ; note\n[s] ; header comment\nk:v\n");
        yield return FuzzSeed.FromText("no-inline", [0, 0], "a=b;c\n  indented = value  \n[ spaced section ]\nName[fr]=nom\n");
        yield return FuzzSeed.FromText("keys-without-value", [4, 1], "[mysqld]\nskip-networking\nport=3306\n[mysqld]\nskip-networking\n");
        yield return FuzzSeed.FromText("multiline", [8, 1], "[paths]\nlist = first\n    second\n\n    # comment\n    third\nother=x\n    \nnext=y\n");
        yield return FuzzSeed.FromText("duplicates", [32 | 64, 7], "[A]\nk=1\nK=2\n[a]\nk=3\n[A]\nk=4\n");
        yield return FuzzSeed.FromText("crlf", [16, 1], "\uFEFF[s]\r\nk=\"v\"\r\n\r\n[t]\rk2=v2\r");
        yield return Seed("set-multiline", 8, 3, "[s]\nk = a\n    b\n", "x\ny\r\nz");
        yield return Seed("set-new-section", 0, 255, "[s]\nk=v\n; trailing comment\n", " needs quotes ");

        static FuzzSeed Seed(string name, byte flags, byte edit, string text, string value)
            => new(name, (byte[])[flags, edit, .. FuzzText.Encode(text), ValueSeparator, .. FuzzText.Encode(value)]);
    }

    private sealed record Layout(List<IniPropertySyntax> Global, List<(IniSectionSyntax Section, List<IniPropertySyntax> Properties)> Sections);

    private sealed class CountingWalker() : IniSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int TokenCount { get; private set; }

        public int TriviaCount { get; private set; }

        public override void VisitToken(SyntaxToken token)
        {
            TokenCount++;
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia) => TriviaCount++;
    }
}
