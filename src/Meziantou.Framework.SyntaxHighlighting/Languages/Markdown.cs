using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Markdown
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var root = new Mode { };
        var section = new Mode { Scope = "section" };
        var atxHeader = new Mode { Begin = "^#{1,6}", End = "$" };
        var htmlTag = new Mode { Begin = "<\\/?[A-Za-z_]", End = ">", SubLanguage = "xml" };
        var link = new Mode { ReturnBegin = true };
        var emptyLinkText = new Mode { Match = "\\[(?=\\])" };
        var linkText = new Mode { Scope = "string", Begin = "\\[", End = "\\]", ExcludeBegin = true, ReturnEnd = true };
        var linkUrl = new Mode { Scope = "link", Begin = "\\]\\(", End = "\\)", ExcludeBegin = true, ExcludeEnd = true };
        var linkRef = new Mode { Scope = "symbol", Begin = "\\]\\[", End = "\\]", ExcludeBegin = true, ExcludeEnd = true };
        // For each of these links, if the first `]` + `[`/`(` (with its scheme) after a `[` is not followed
        // by the closing bracket on the same line, no later one is. So the atomic groups stop at the
        // first candidate, and only the first `[` of a line (or after the scan start) may start a match:
        // if it fails, every later `[` of the line fails too.
        const string FirstBracketOfLine = "\\[(?<=(?:\\G|^)[^\\[\\n]*\\[)";
        var referenceLink = new Mode { Begin = FirstBracketOfLine + "(?>.+?\\]\\[).*?\\]" };
        var safeUrlLink = new Mode { Begin = FirstBracketOfLine + "(?>.+?\\]\\(((data|javascript|mailto):|(?:http|ftp)s?:\\/\\/)).*?\\)" };
        var schemeUrlLink = new Mode { Begin = FirstBracketOfLine + "(?>.+?\\]\\([A-Za-z][A-Za-z0-9+.-]*:\\/\\/).*?\\)" };
        var relativeUrlLink = new Mode { Begin = FirstBracketOfLine + "(?>.+?\\]\\([./?&#]).*?\\)" };
        var anyUrlLink = new Mode { Begin = FirstBracketOfLine + "(?>.*?\\]\\().*?\\)" };
        var strong = new Mode { Scope = "strong" };
        var strongInner = new Mode { Scope = "emphasis" };
        var emphasisStar = new Mode { Begin = "\\*(?![*\\s])", End = "\\*" };
        var emphasisUnderscore = new Mode { Begin = "_(?![_\\s])", End = "_" };
        var strongUnderscore = new Mode { Begin = "_{2}(?!\\s)", End = "_{2}" };
        var strongStar = new Mode { Begin = "\\*{2}(?!\\s)", End = "\\*{2}" };
        var emphasis = new Mode { Scope = "emphasis" };
        var emphasisInner = new Mode { Scope = "strong" };
        var setextHeader = new Mode { Begin = "(?=^.+?\\n[=-]{2,}$)" };
        var setextUnderline = new Mode { Begin = "^[=-]*$" };
        var setextLine = new Mode { Begin = "^", End = "\\n" };
        var listBullet = new Mode { Scope = "bullet", Begin = "^[ \t]*([*+-]|(\\d+\\.))(?=\\s+)", End = "\\s+", ExcludeEnd = true };
        var blockquote = new Mode { Scope = "quote", Begin = "^>\\s+", End = "$" };
        var code = new Mode { Scope = "code" };
        // A fence is limited to 64 characters: from each position of a longer run, the opening fence would
        // rescan the rest of the run. Only the first character of a run may start a closing fence: the
        // later ones would end at the same place.
        var fencedCodeBacktick = new Mode { Begin = "((?>`{3,64}))[^`](.|\\n)*?\\1`*[ ]*" };
        var fencedCodeTilde = new Mode { Begin = "((?>~{3,64}))[^~](.|\\n)*?\\1~*[ ]*" };
        var fencedCodeBacktickAlt = new Mode { Begin = "```", End = "(?:\\G|(?<!`))```+[ ]*$" };
        var fencedCodeTildeAlt = new Mode { Begin = "~~~", End = "(?:\\G|(?<!~))~~~+[ ]*$" };
        var inlineCode = new Mode { Begin = "`.+?`" };
        var indentedCode = new Mode { Begin = "(?=^( {4}|\\t))" };
        var indentedCodeBlock = new Mode { Begin = "^( {4}|\\t)", End = "(\\n)$" };
        var horizontalRule = new Mode { Begin = "^[-\\*]{3,}", End = "$" };
        var linkDefinition = new Mode { Begin = "^\\[[^\\n]+\\]:", ReturnBegin = true };
        var linkDefSymbol = new Mode { Scope = "symbol", Begin = "\\[", End = "\\]", ExcludeBegin = true, ExcludeEnd = true };
        var linkDefUrl = new Mode { Scope = "link", Begin = ":\\s*", End = "$", ExcludeBegin = true };
        var htmlEntity = new Mode { Scope = "literal", Match = "&([a-zA-Z0-9]+|#[0-9]{1,7}|#[Xx][0-9a-fA-F]{1,6});" };

        root.Contains = [section, htmlTag, listBullet, strong, emphasis, blockquote, code, horizontalRule, link, linkDefinition, htmlEntity];
        section.Variants = [atxHeader, setextHeader];
        atxHeader.Contains = [htmlTag, link, strong, emphasis];
        link.Contains = [emptyLinkText, linkText, linkUrl, linkRef];
        link.Variants = [referenceLink, safeUrlLink, schemeUrlLink, relativeUrlLink, anyUrlLink];
        strong.Contains = [strongInner, htmlTag, link];
        strong.Variants = [strongUnderscore, strongStar];
        strongInner.Contains = [htmlTag, link];
        strongInner.Variants = [emphasisStar, emphasisUnderscore];
        emphasis.Contains = [emphasisInner, htmlTag, link];
        emphasis.Variants = [emphasisStar, emphasisUnderscore];
        emphasisInner.Contains = [htmlTag, link];
        emphasisInner.Variants = [strongUnderscore, strongStar];
        setextHeader.Contains = [setextUnderline, setextLine];
        setextLine.Contains = [htmlTag, link, strong, emphasis];
        blockquote.Contains = [htmlTag, link, strong, emphasis];
        code.Variants = [fencedCodeBacktick, fencedCodeTilde, fencedCodeBacktickAlt, fencedCodeTildeAlt, inlineCode, indentedCode];
        indentedCode.Contains = [indentedCodeBlock];
        linkDefinition.Contains = [linkDefSymbol, linkDefUrl];

        return root;
    }
}
