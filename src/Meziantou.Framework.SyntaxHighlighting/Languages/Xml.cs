using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Xml
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var root = new Mode { CaseInsensitive = true };
        var docTypeDecl = new Mode { Scope = "meta", Begin = "<![a-z]", End = ">" };
        var whitespace = new Mode { Begin = "\\s" };
        var docTypeKeyword = new Mode { Scope = "keyword", Begin = "#?[a-z_][a-z1-9_-]+", Illegal = "\\n" };
        var doubleQuotedString = new Mode { Scope = "string", Begin = "\"", End = "\"", Illegal = "\\n" };
        var escapeSequence = new Mode { Begin = "\\\\[\\s\\S]" };
        var singleQuotedString = new Mode { Scope = "string", Begin = "'", End = "'", Illegal = "\\n" };
        var parenGroup = new Mode { Begin = "\\(", End = "\\)" };
        var bracketGroup = new Mode { Begin = "\\[", End = "\\]" };
        var nestedDocType = new Mode { Scope = "meta", Begin = "<![a-z]", End = ">" };
        var comment = new Mode { Scope = "comment", Begin = "<!--", End = "-->" };
        var docTag = new Mode { Scope = "doctag", Begin = "[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)", End = "(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):", ExcludeBegin = true };
        var commentSentence = new Mode { Begin = "[ ]+((?:I|a|is|so|us|to|at|if|in|it|on|[A-Za-z]+['](d|ve|re|ll|t|s|n)|[A-Za-z]+[-][a-z]+|[A-Za-z][a-z]{2,})[.]?[:]?([.][ ]|[ ])){3}" };
        var cdata = new Mode { Begin = "<!\\[CDATA\\[", End = "\\]\\]>" };
        var entity = new Mode { Scope = "symbol", Begin = "&[a-z]+;|&#[0-9]+;|&#x[a-f0-9]+;" };
        var processingInstruction = new Mode { Scope = "meta", End = "\\?>" };
        var xmlDeclaration = new Mode { Begin = "<\\?xml" };
        var otherProcessingInstruction = new Mode { Begin = "<\\?[a-z][a-z0-9]+" };
        var fragmentTag = new Mode { Scope = "tag", Begin = "<>|<\\/>" };
        var openTag = new Mode { Scope = "tag", Begin = "<(?=[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*(?:\\/>|>|\\s))", End = "\\/?>" };
        var tagName = new Mode { Scope = "name", Begin = "[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*" };
        var tagAttributes = new Mode { Illegal = "<", EndsWithParent = true };
        var attrName = new Mode { Scope = "attr", Begin = "[\\p{L}0-9._:-]+" };
        var attrAssignment = new Mode { Begin = "=\\s*" };
        var attrValue = new Mode { Scope = "string", EndsParent = true };
        var attrDoubleQuoted = new Mode { Begin = "\"", End = "\"" };
        var attrSingleQuoted = new Mode { Begin = "'", End = "'" };
        var attrUnquoted = new Mode { Begin = "[^\\s\"'=<>`]+" };
        var closeTag = new Mode { Scope = "tag", Begin = "<\\/(?=[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*>)" };
        var closeTagName = new Mode { Scope = "name", Begin = "[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*" };
        var closeTagEnd = new Mode { Begin = ">", EndsParent = true };

        root.Contains = [docTypeDecl, comment, cdata, entity, processingInstruction, fragmentTag, openTag, closeTag];
        docTypeDecl.Contains = [whitespace, doubleQuotedString, singleQuotedString, parenGroup, bracketGroup];
        whitespace.Contains = [docTypeKeyword];
        doubleQuotedString.Contains = [escapeSequence];
        singleQuotedString.Contains = [escapeSequence];
        parenGroup.Contains = [docTypeKeyword];
        bracketGroup.Contains = [nestedDocType];
        nestedDocType.Contains = [whitespace, parenGroup, doubleQuotedString, singleQuotedString];
        comment.Contains = [docTag, commentSentence];
        processingInstruction.Variants = [xmlDeclaration, otherProcessingInstruction];
        xmlDeclaration.Contains = [doubleQuotedString];
        openTag.Contains = [tagName];
        tagName.Starts = tagAttributes;
        tagAttributes.Contains = [attrName, attrAssignment];
        attrAssignment.Contains = [attrValue];
        attrValue.Variants = [attrDoubleQuoted, attrSingleQuoted, attrUnquoted];
        attrDoubleQuoted.Contains = [entity];
        attrSingleQuoted.Contains = [entity];
        closeTag.Contains = [closeTagName, closeTagEnd];

        return root;
    }
}
