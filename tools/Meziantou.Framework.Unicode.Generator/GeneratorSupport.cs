#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Text.RegularExpressions;
using Meziantou.Framework;

internal static class GeneratorFiles
{
    public static async Task<bool> WriteTextIfChanged(FullPath filePath, string content)
    {
        if (File.Exists(filePath) && (await File.ReadAllTextAsync(filePath)).ReplaceLineEndings("\n") == content.ReplaceLineEndings("\n"))
            return false;

        filePath.CreateParentDirectory();
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(filePath, content, encoding);
        return true;
    }

    /// <summary>Converts a property value name such as <c>Mandatory_Break</c> to a C# identifier.</summary>
    public static string ToIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        var upperNext = true;
        foreach (var c in name)
        {
            if (c == '_')
            {
                upperNext = true;
                continue;
            }

            sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }

        var identifier = sb.ToString();
        if (identifier.Length == 0 || !char.IsLetter(identifier[0]))
            throw new InvalidOperationException($"The name '{name}' does not yield a valid C# identifier");

        return identifier;
    }

    public static string EscapeXmlText(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}

/// <summary>Assigns the numeric values of a generated enumeration so that a value never changes once published.</summary>
/// <remarks>
/// Other assemblies compile these constants in, and may then run with a newer version of this package: a member
/// keeps the value it has in the committed file, and a new member takes the next free one.
/// </remarks>
internal static partial class StableEnum
{
    /// <summary>Gets the value of each member, by identifier.</summary>
    /// <param name="filePath">The committed generated file that declares the enumeration, when it exists.</param>
    /// <param name="enumName">The name of the enumeration.</param>
    /// <param name="identifiers">The members that must exist; the first one takes 0 when the enumeration is new.</param>
    public static Dictionary<string, int> Resolve(FullPath filePath, string enumName, IEnumerable<string> identifiers)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (File.Exists(filePath))
        {
            var content = File.ReadAllText(filePath);
            var declaration = Regex.Match(content, @"\benum\s+" + Regex.Escape(enumName) + @"\b[^{]*\{(?<body>[^}]*)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(10));
            if (declaration.Success)
            {
                foreach (Match member in MemberRegex().Matches(declaration.Groups["body"].Value))
                {
                    result.Add(member.Groups["name"].Value, int.Parse(member.Groups["value"].Value, CultureInfo.InvariantCulture));
                }
            }
        }

        var next = result.Count == 0 ? 0 : result.Values.Max() + 1;
        foreach (var identifier in identifiers)
        {
            if (result.ContainsKey(identifier))
                continue;

            result.Add(identifier, next);
            next++;
        }

        return result;
    }

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>[0-9]+)\s*,", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex MemberRegex();
}

internal static class UcdParsers
{
    public static UnicodeCategory ParseUnicodeCategory(string value)
    {
        return value switch
        {
            "Lu" => UnicodeCategory.UppercaseLetter,
            "Ll" => UnicodeCategory.LowercaseLetter,
            "Lt" => UnicodeCategory.TitlecaseLetter,
            "Lm" => UnicodeCategory.ModifierLetter,
            "Lo" => UnicodeCategory.OtherLetter,
            "Mn" => UnicodeCategory.NonSpacingMark,
            "Mc" => UnicodeCategory.SpacingCombiningMark,
            "Me" => UnicodeCategory.EnclosingMark,
            "Nd" => UnicodeCategory.DecimalDigitNumber,
            "Nl" => UnicodeCategory.LetterNumber,
            "No" => UnicodeCategory.OtherNumber,
            "Pc" => UnicodeCategory.ConnectorPunctuation,
            "Pd" => UnicodeCategory.DashPunctuation,
            "Ps" => UnicodeCategory.OpenPunctuation,
            "Pe" => UnicodeCategory.ClosePunctuation,
            "Pi" => UnicodeCategory.InitialQuotePunctuation,
            "Pf" => UnicodeCategory.FinalQuotePunctuation,
            "Po" => UnicodeCategory.OtherPunctuation,
            "Sm" => UnicodeCategory.MathSymbol,
            "Sc" => UnicodeCategory.CurrencySymbol,
            "Sk" => UnicodeCategory.ModifierSymbol,
            "So" => UnicodeCategory.OtherSymbol,
            "Zs" => UnicodeCategory.SpaceSeparator,
            "Zl" => UnicodeCategory.LineSeparator,
            "Zp" => UnicodeCategory.ParagraphSeparator,
            "Cc" => UnicodeCategory.Control,
            "Cf" => UnicodeCategory.Format,
            "Cs" => UnicodeCategory.Surrogate,
            "Co" => UnicodeCategory.PrivateUse,
            "Cn" => UnicodeCategory.OtherNotAssigned,
            _ => throw new InvalidOperationException("Unknown Unicode category: " + value),
        };
    }

    public static UnicodeBidirectionalCategory ParseBidirectionalCategory(string value)
    {
        return value switch
        {
            "L" => UnicodeBidirectionalCategory.LeftToRight,
            "R" => UnicodeBidirectionalCategory.RightToLeft,
            "AL" => UnicodeBidirectionalCategory.RightToLeftArabic,
            "EN" => UnicodeBidirectionalCategory.EuropeanNumber,
            "ES" => UnicodeBidirectionalCategory.EuropeanSeparator,
            "ET" => UnicodeBidirectionalCategory.EuropeanTerminator,
            "AN" => UnicodeBidirectionalCategory.ArabicNumber,
            "CS" => UnicodeBidirectionalCategory.CommonSeparator,
            "B" => UnicodeBidirectionalCategory.ParagraphSeparator,
            "S" => UnicodeBidirectionalCategory.SegmentSeparator,
            "WS" => UnicodeBidirectionalCategory.WhiteSpace,
            "ON" => UnicodeBidirectionalCategory.OtherNeutral,
            "NSM" => UnicodeBidirectionalCategory.NonspacingMark,
            "LRE" => UnicodeBidirectionalCategory.LeftToRightEmbedding,
            "LRO" => UnicodeBidirectionalCategory.LeftToRightOverride,
            "RLE" => UnicodeBidirectionalCategory.RightToLeftEmbedding,
            "RLO" => UnicodeBidirectionalCategory.RightToLeftOverride,
            "PDF" => UnicodeBidirectionalCategory.PopDirectionalFormat,
            "LRI" => UnicodeBidirectionalCategory.LeftToRightIsolate,
            "RLI" => UnicodeBidirectionalCategory.RightToLeftIsolate,
            "FSI" => UnicodeBidirectionalCategory.FirstStrongIsolate,
            "PDI" => UnicodeBidirectionalCategory.PopDirectionalIsolate,
            "BN" => UnicodeBidirectionalCategory.BoundaryNeutral,
            _ => throw new InvalidOperationException("Unknown bidirectional category: " + value),
        };
    }
}
