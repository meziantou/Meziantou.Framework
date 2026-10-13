using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Transcribes the presentation forms of the Arabic letters from the Unicode Character Database into the source of the
/// library: for a letter and the form it takes in a word, the character of the Arabic Presentation Forms blocks that
/// is the letter in that form.
/// </summary>
/// <remarks>
/// <para>
/// The source is <c>UnicodeData.txt</c> (<see cref="UnicodeDataUrl"/>), which is not committed and is pinned by its
/// SHA-256: the version of Unicode is the one of <c>Meziantou.Framework.Unicode</c>. A character of the blocks
/// U+FB50 to U+FDFF and U+FE70 to U+FEFF has a compatibility decomposition tagged with its form
/// (<c>&lt;isolated&gt;</c>, <c>&lt;final&gt;</c>, <c>&lt;initial&gt;</c>, <c>&lt;medial&gt;</c>).
/// </para>
/// <para>
/// Two tables are written: the characters that are one letter in a form, and the ligatures of two letters of the
/// block Arabic Presentation Forms-B, which are the ligatures of lam and alef that Arabic requires. The ligatures of
/// the block Arabic Presentation Forms-A are those of a typographic tradition, and are not forms a text needs.
/// </para>
/// </remarks>
internal static class ArabicFormTable
{
    private const string UnicodeVersion = "18.0.0";
    private const string UnicodeDataUrl = "https://www.unicode.org/Public/" + UnicodeVersion + "/ucd/UnicodeData.txt";
    private const string UnicodeDataSha256 = "0736451de439ae7baf1425136617da495e09ee5afbe6e394374db7009ea08950";
    private const string TableSource = "src/Meziantou.Framework.Imaging/Internals/Text/Shaping/ArabicPresentationForms.g.cs";

    // The forms, as the library numbers them.
    private static readonly string[] Tags = ["<isolated>", "<final>", "<initial>", "<medial>"];

    public static int Run(GeneratorOptions options)
    {
        var bytes = File.ReadAllBytes(options.UnicodeData!);
        var hash = Bytes.Sha256Hex(bytes);
        if (!string.Equals(hash, UnicodeDataSha256, StringComparison.Ordinal))
            throw new FatalException($"UnicodeData.txt has sha256 {hash} but {UnicodeDataSha256} is pinned ({UnicodeDataUrl}).");

        var forms = new SortedDictionary<(int Letter, int Form), int>();
        var ligatures = new SortedDictionary<(int First, int Second, int Form), int>();
        foreach (var line in Encoding.ASCII.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(';');
            var character = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (character is not ((>= 0xFB50 and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF)))
                continue;

            var decomposition = fields[5].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var form = decomposition.Length == 0 ? -1 : Array.IndexOf(Tags, decomposition[0]);
            if (form < 0)
                continue;

            var letters = decomposition.Skip(1).Select(value => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            if (letters.Any(letter => letter > 0xFFFF))
                throw new FatalException($"U+{character:X4} decomposes to a character outside the Basic Multilingual Plane.");

            if (letters.Length == 1)
            {
                if (!forms.TryAdd((letters[0], form), character))
                    throw new FatalException($"U+{letters[0]:X4} has two characters for the form {Tags[form]}: U+{forms[(letters[0], form)]:X4} and U+{character:X4}.");
            }
            else if (letters.Length == 2 && character >= 0xFE70 && letters[0] is not (0x0020 or 0x0640))
            {
                // The other characters of two parts of the block are a mark on a space or on a tatweel.
                ligatures.Add((letters[0], letters[1], form), character);
            }
        }

        if (forms.Count < 200 || ligatures.Count != 8)
            throw new FatalException($"UnicodeData.txt gives {forms.Count} forms and {ligatures.Count} ligatures: the file is not the one of Unicode {UnicodeVersion}.");

        var source = Render(forms, ligatures, hash);
        var target = CorpusDriver.RepoRoot / TableSource;
        if (File.Exists(target) && string.Equals(File.ReadAllText(target, Encoding.UTF8), source, StringComparison.Ordinal))
        {
            Console.WriteLine($"{TableSource} is reproducible ({forms.Count} forms, {ligatures.Count} ligatures).");
            return 0;
        }

        if (!options.Write)
        {
            Console.WriteLine($"{TableSource} differs from the generator output. Review, then rerun with --write.");
            return 1;
        }

        File.WriteAllText(target, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"{TableSource} written ({forms.Count} forms, {ligatures.Count} ligatures). Review the diff before committing.");
        return 0;
    }

    private static string Render(SortedDictionary<(int Letter, int Form), int> forms, SortedDictionary<(int First, int Second, int Form), int> ligatures, string hash)
    {
        var builder = new StringBuilder();
        builder.Append("// <auto-generated>\n");
        builder.Append("// Transcribed by tools/Meziantou.Framework.Imaging.CorpusGenerator (arabic-forms) from the compatibility\n");
        builder.Append(CultureInfo.InvariantCulture, $"// decompositions of UnicodeData.txt of Unicode {UnicodeVersion} ({UnicodeDataUrl},\n");
        builder.Append(CultureInfo.InvariantCulture, $"// SHA-256 {hash}).\n");
        builder.Append("// Do not edit this file: run the generator again.\n");
        builder.Append("// </auto-generated>\n\n");
        builder.Append("namespace Meziantou.Framework.Imaging.Internals;\n\n");
        builder.Append("internal static partial class ArabicShaper\n{\n");
        builder.Append(CultureInfo.InvariantCulture, $"    private const int PresentationFormCount = {forms.Count};\n\n");
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Gets the characters that are a letter in one of its forms, sorted by letter then by form: records of five bytes,\n");
        builder.Append("    /// the letter on 16 bits, the form (0 isolated, 1 final, 2 initial, 3 medial), the character on 16 bits.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    private static ReadOnlySpan<byte> PresentationForms =>\n    [\n");
        foreach (var ((letter, form), character) in forms)
        {
            builder.Append(CultureInfo.InvariantCulture, $"        0x{letter >> 8:X2}, 0x{letter & 0xFF:X2}, {form}, 0x{character >> 8:X2}, 0x{character & 0xFF:X2},\n");
        }

        builder.Append("    ];\n\n");
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Gets the characters that are a ligature of two letters, the second one in its final form: records of seven\n");
        builder.Append("    /// bytes, the two letters on 16 bits each, the form of the ligature (0 isolated, 1 final), the character on 16 bits.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    private static ReadOnlySpan<byte> PresentationLigatures =>\n    [\n");
        foreach (var ((first, second, form), character) in ligatures)
        {
            builder.Append(CultureInfo.InvariantCulture, $"        0x{first >> 8:X2}, 0x{first & 0xFF:X2}, 0x{second >> 8:X2}, 0x{second & 0xFF:X2}, {form}, 0x{character >> 8:X2}, 0x{character & 0xFF:X2},\n");
        }

        builder.Append("    ];\n}\n");
        return builder.ToString();
    }
}
