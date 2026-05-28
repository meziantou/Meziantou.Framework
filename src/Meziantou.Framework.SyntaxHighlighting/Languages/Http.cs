using Meziantou.Framework.SyntaxHighlighting.Engine;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Http
{
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var root = new Mode { Illegal = "\\S" };
        var responseStatusLine = new Mode { Begin = "^(?=HTTP/([32]|1\\.[01]) \\d{3})", End = "$" };
        var responseVersion = new Mode { Scope = "meta", Begin = "HTTP/([32]|1\\.[01])" };
        var statusCode = new Mode { Scope = "number", Begin = "\\b\\d{3}\\b" };
        var responseHeaders = new Mode { End = "\\b\\B", Illegal = "\\S" };
        var headerName = new Mode { Scope = "attribute", Begin = "^[A-Za-z][A-Za-z0-9-]*(?=\\:\\s)" };
        var headerColon = new Mode { };
        var headerSeparator = new Mode { Scope = "punctuation", Begin = ": " };
        var headerValue = new Mode { End = "$" };
        var genericBody = new Mode { Begin = "\\n\\n" };
        var genericBodyContent = new Mode { EndsWithParent = true };
        var jsonBodyStart = new Mode { Begin = "\\n\\n(?=\\s*[{\\[])" };
        var jsonBodyContent = new Mode { SubLanguage = "json", EndsWithParent = true };
        var xmlBodyStart = new Mode { Begin = "\\n\\n(?=\\s*<)" };
        var xmlBodyContent = new Mode { SubLanguage = "xml", EndsWithParent = true };
        var formBodyStart = new Mode { Begin = "\\n\\n(?=[A-Za-z0-9_%][A-Za-z0-9_%.+-]*=)" };
        var formBodyContent = new Mode { SubLanguage = "urlencoded", EndsWithParent = true };
        var requestLine = new Mode { Begin = "(?=^[A-Z]+ (.*?) HTTP/([32]|1\\.[01])$)", End = "$" };
        var requestUri = new Mode { Scope = "string", Begin = " ", End = " ", ExcludeBegin = true, ExcludeEnd = true };
        var requestVersion = new Mode { Scope = "meta", Begin = "HTTP/([32]|1\\.[01])" };
        var requestMethod = new Mode { Scope = "keyword", Begin = "[A-Z]+" };
        var requestHeaders = new Mode { End = "\\b\\B", Illegal = "\\S" };
        var orphanHeaderName = new Mode { Scope = "attribute", Begin = "^[A-Za-z][A-Za-z0-9-]*(?=\\:\\s)" };

        root.Contains = [responseStatusLine, requestLine, orphanHeaderName];
        responseStatusLine.Contains = [responseVersion, statusCode];
        responseStatusLine.Starts = responseHeaders;
        responseHeaders.Contains = [headerName, jsonBodyStart, xmlBodyStart, formBodyStart, genericBody];
        headerName.Starts = headerColon;
        headerColon.Contains = [headerSeparator];
        headerSeparator.Starts = headerValue;
        genericBody.Starts = genericBodyContent;
        jsonBodyStart.Starts = jsonBodyContent;
        xmlBodyStart.Starts = xmlBodyContent;
        formBodyStart.Starts = formBodyContent;
        requestLine.Contains = [requestUri, requestVersion, requestMethod];
        requestLine.Starts = requestHeaders;
        requestHeaders.Contains = [headerName, jsonBodyStart, xmlBodyStart, formBodyStart, genericBody];
        orphanHeaderName.Starts = headerColon;

        return root;
    }
}
