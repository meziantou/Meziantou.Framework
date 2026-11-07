using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages.Common;

/// <summary>
/// The patterns and modes that the highlight.js C and C++ grammars share.
/// </summary>
internal static class CFamily
{
    public const string DeclTypeAutoRe = @"decltype\(auto\)";
    public const string NamespaceRe = @"[a-zA-Z_]\w*::";
    private const string TemplateArgumentRe = @"<[^<>]+>";

    // https://en.cppreference.com/w/cpp/language/escape
    private const string CharacterEscapesRe = @"\\(x[0-9A-Fa-f]{2}|u[0-9A-Fa-f]{4,8}|[0-7]{3}|\S)";

    /// <summary>A system header name of a preprocessor directive (<c>&lt;stdio.h&gt;</c>).</summary>
    /// <remarks>A <c>&lt;</c> that follows an unclosed <c>&lt;</c> of the same line would end on the same <c>&gt;</c>.</remarks>
    public const string HeaderNameRe = @"(?=<)(?:\G|(?<!<(?:(?!\G)[^>\n])*?))<.*?>";

    // The namespace and the identifier are both runs of word characters that are searched for: only try
    // each of them from the first position of the run where it can start.
    public static readonly string ScopedIdentifierRe =
        "(?:" + CommonModes.RunStart(@"\w", "a-zA-Z_") + NamespaceRe + ")?" + CommonModes.IdentRe;

    public static readonly string FunctionTitleRe = CreateFunctionTitleRe(titleGuard: null);

    /// <summary>An identifier followed by <c>::</c>.</summary>
    public static readonly string ScopeQualifierRe = CommonModes.RunStart(@"\w", "a-zA-Z") + CommonModes.IdentRe + "::";

    /// <summary>
    /// highlight.js's <c>FUNCTION_TITLE</c>, where <paramref name="titleGuard"/> is a lookahead that the name must satisfy.
    /// </summary>
    public static string CreateFunctionTitleRe(string? titleGuard) =>
        "(?:" + CommonModes.RunStart(@"\w", "a-zA-Z_") + NamespaceRe + ")?" + CommonModes.RunStart(@"\w", "a-zA-Z") + titleGuard + CommonModes.IdentRe + @"\s*\(";

    /// <summary>
    /// highlight.js's <c>'(' + FUNCTION_TYPE_RE + '[\\*&amp;\\s]+)+' + FUNCTION_TITLE</c>, where
    /// <paramref name="typeGuard"/> is a lookahead that every type must satisfy (C++ uses <c>(?!struct)</c>) and
    /// <paramref name="titleGuard"/> one that the name must satisfy.
    /// </summary>
    /// <remarks>
    /// A declaration is a sequence of types followed by the function name. A type that follows another
    /// type of the sequence can also be matched from the start of the sequence, so it can never be the
    /// leftmost match and is skipped: otherwise, each type of a long sequence would rescan it. The
    /// previous type does not count when the scan starts after it (e.g. after a preprocessor directive).
    /// </remarks>
    public static string CreateFunctionDeclarationRe(string? typeGuard, string? titleGuard = null)
    {
        var functionTypeRe = "(" + DeclTypeAutoRe + "|(?:" + NamespaceRe + @")?[a-zA-Z_]\w*(?:" + TemplateArgumentRe + ")?)";
        var functionTypeNoCaptureRe = "(?:" + DeclTypeAutoRe + "|(?:" + NamespaceRe + @")?[a-zA-Z_]\w*(?:" + TemplateArgumentRe + ")?)";

        return CommonModes.RunStart(@"\w", "a-zA-Z_", typeGuard)
            + @"(?:\G|(?<!" + typeGuard + functionTypeNoCaptureRe + @"(?:(?!\G)[\*&\s])+))"
            + "(" + typeGuard + functionTypeRe + @"[\*&\s]+)+" + CreateFunctionTitleRe(titleGuard);
    }

    public static Mode CreateStrings() => new()
    {
        Scope = "string",
        Variants =
        [
            new Mode
            {
                Begin = @"(u8?|U|L)?""",
                End = @"""",
                Illegal = @"\n",
                Contains = [CommonModes.BackslashEscape],
            },
            new Mode
            {
                Begin = @"(u8?|U|L)?'(" + CharacterEscapesRe + "|.)",
                End = "'",
                Illegal = ".",
            },
            new Mode
            {
                Begin = @"(?:u8?|U|L)?R""([^()\\ ]{0,16})\(",
                End = @"\)([^()\\ ]{0,16})""",
                EndSameAsBegin = true,
            },
        ],
    };
}
