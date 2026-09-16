using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Html
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var root = new Mode { CaseInsensitive = true };

        // Document type declaration: `<!DOCTYPE html>`, including the internal subset of a DTD.
        var docType = new Mode { Scope = "meta", Begin = "<![a-z]", End = ">" };
        var docTypeKeywordSpace = new Mode { Begin = "\\s" };
        var docTypeKeyword = new Mode { Scope = "keyword", Begin = "#?[a-z_][a-z1-9_-]+", Illegal = "\\n" };
        var docTypeDoubleQuotedString = new Mode { Scope = "string", Begin = "\"", End = "\"", Illegal = "\\n" };
        var docTypeEscape = new Mode { Begin = "\\\\[\\s\\S]" };
        var docTypeSingleQuotedString = new Mode { Scope = "string", Begin = "'", End = "'", Illegal = "\\n" };
        var docTypeParens = new Mode { Begin = "\\(", End = "\\)" };
        var docTypeInternalSubset = new Mode { Begin = "\\[", End = "\\]" };
        var docTypeNestedDeclaration = new Mode { Scope = "meta", Begin = "<![a-z]", End = ">" };

        var comment = new Mode { Scope = "comment", Begin = "<!--", End = "-->" };
        var commentDocTag = new Mode { Scope = "doctag", Begin = "[ ]*(?=(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):)", End = "(TODO|FIXME|NOTE|BUG|OPTIMIZE|HACK|XXX):", ExcludeBegin = true };
        var commentSentence = new Mode { Begin = "[ ]+((?:I|a|is|so|us|to|at|if|in|it|on|[A-Za-z]+['](d|ve|re|ll|t|s|n)|[A-Za-z]+[-][a-z]+|[A-Za-z][a-z]{2,})[.]?[:]?([.][ ]|[ ])){3}" };

        var cdata = new Mode { Begin = "<!\\[CDATA\\[", End = "\\]\\]>" };
        var entity = new Mode { Scope = "symbol", Begin = "&[a-z]+;|&#[0-9]+;|&#x[a-f0-9]+;" };

        // Processing instructions: `<?xml … ?>` and `<?php … ?>`-style.
        var processingInstruction = new Mode { Scope = "meta", End = "\\?>" };
        var xmlDeclaration = new Mode { Begin = "<\\?xml" };
        var otherProcessingInstruction = new Mode { Begin = "<\\?[a-z][a-z0-9]+" };

        var styleTag = new Mode { Scope = "tag", Begin = "<style(?=\\s|>)", End = ">", Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["name"] = ["style"] }) };
        var styleBody = new Mode { End = "<\\/style>", ReturnEnd = true, SubLanguage = "css" };
        var scriptTag = new Mode { Scope = "tag", Begin = "<script(?=\\s|>)", End = ">", Keywords = Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["name"] = ["script"] }) };
        var scriptBody = new Mode { End = "<\\/script>", ReturnEnd = true, SubLanguage = "javascript" };

        // The attributes of an open tag, and the values they can take.
        var attributes = new Mode { Illegal = "<", EndsWithParent = true };
        var attributeName = new Mode { Scope = "attr", Begin = "[\\p{L}0-9._:-]+" };
        var attributeAssignment = new Mode { Begin = "=\\s*" };
        var attributeValue = new Mode { Scope = "string", EndsParent = true };
        var doubleQuotedValue = new Mode { Begin = "\"", End = "\"" };
        var singleQuotedValue = new Mode { Begin = "'", End = "'" };
        var unquotedValue = new Mode { Begin = "[^\\s\"'=<>`]+" };

        var fragmentTag = new Mode { Scope = "tag", Begin = "<>|<\\/>" };
        var openTag = new Mode { Scope = "tag", Begin = "<(?=[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*(?:\\/>|>|\\s))", End = "\\/?>" };
        var openTagName = new Mode { Scope = "name", Begin = "[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*" };
        var closeTag = new Mode { Scope = "tag", Begin = "<\\/(?=[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*>)" };
        var closeTagName = new Mode { Scope = "name", Begin = "[\\p{L}_](?:[\\p{L}0-9_.-]*:)?[\\p{L}0-9_.-]*" };
        var closeTagEnd = new Mode { Begin = ">", EndsParent = true };

        root.Contains = [docType, comment, cdata, entity, processingInstruction, styleTag, scriptTag, fragmentTag, openTag, closeTag];
        docType.Contains = [docTypeKeywordSpace, docTypeDoubleQuotedString, docTypeSingleQuotedString, docTypeParens, docTypeInternalSubset];
        docTypeKeywordSpace.Contains = [docTypeKeyword];
        docTypeDoubleQuotedString.Contains = [docTypeEscape];
        docTypeSingleQuotedString.Contains = [docTypeEscape];
        docTypeParens.Contains = [docTypeKeyword];
        docTypeInternalSubset.Contains = [docTypeNestedDeclaration];
        docTypeNestedDeclaration.Contains = [docTypeKeywordSpace, docTypeParens, docTypeDoubleQuotedString, docTypeSingleQuotedString];
        comment.Contains = [commentDocTag, commentSentence];
        processingInstruction.Variants = [xmlDeclaration, otherProcessingInstruction];
        xmlDeclaration.Contains = [docTypeDoubleQuotedString];
        styleTag.Contains = [attributes];
        styleTag.Starts = styleBody;
        attributes.Contains = [attributeName, attributeAssignment];
        attributeAssignment.Contains = [attributeValue];
        attributeValue.Variants = [doubleQuotedValue, singleQuotedValue, unquotedValue];
        doubleQuotedValue.Contains = [entity];
        singleQuotedValue.Contains = [entity];
        scriptTag.Contains = [attributes];
        scriptTag.Starts = scriptBody;
        openTag.Contains = [openTagName];
        openTagName.Starts = attributes;
        closeTag.Contains = [closeTagName, closeTagEnd];

        return root;
    }
}
