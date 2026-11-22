using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.Framework.WasmEngine.SourceGeneration;

internal static class CSharpNaming
{
    /// <summary>A PascalCase identifier of a name: the letters and the digits, the words separated by the other characters start with an upper case letter.</summary>
    public static string ToPascalCase(string name, string fallback)
    {
        var builder = new StringBuilder(name.Length);
        var startWord = true;
        foreach (var c in name)
        {
            if (!IsIdentifierCharacter(c))
            {
                startWord = true;
                continue;
            }

            if (builder.Length == 0 && !IsIdentifierStart(c))
            {
                builder.Append('_');
            }

            builder.Append(startWord ? char.ToUpperInvariant(c) : c);
            startWord = false;
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    public static string ToCamelCase(string name, string fallback)
    {
        var pascal = ToPascalCase(name, fallback);
        if (pascal[0] == '_')
            return pascal;

        var result = char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        return SyntaxFacts.GetKeywordKind(result) != SyntaxKind.None ? "@" + result : result;
    }

    /// <summary>Appends a number to the name until it is not in <paramref name="names"/>, and adds it.</summary>
    public static string MakeUnique(string name, HashSet<string> names)
    {
        var result = name;
        for (var i = 2; !names.Add(result); i++)
        {
            result = name + i.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }

    public static bool IsValidIdentifier(string name) => SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;

    // The formatting characters are ignored when C# compares identifiers (a‍b is ab): they are separators
    private static bool IsIdentifierCharacter(char c) => char.GetUnicodeCategory(c) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark;

    private static bool IsIdentifierStart(char c) => char.GetUnicodeCategory(c) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
        UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;
}
