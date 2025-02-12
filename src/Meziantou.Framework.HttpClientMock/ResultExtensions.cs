using System.Text;
using Meziantou.Framework.Internals;
using Microsoft.AspNetCore.Http;

namespace Meziantou.Framework;

public static class ResultExtensions
{
    public static IResult RawJson(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Json)] string content, Encoding? encoding)
    {
        return RawJson(_, content, encoding, statusCode: null);
    }

    public static IResult RawJson(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Json)] string content, Encoding? encoding = null, int? statusCode = null)
    {
        return Results.Text(content, contentType: "application/json", encoding, statusCode);
    }

    public static IResult RawXml(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Xml)] string content, Encoding? encoding)
    {
        return RawXml(_, content, encoding, statusCode: null);
    }

    public static IResult RawXml(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Xml)] string content, Encoding? encoding = null, int? statusCode = null)
    {
        return Results.Text(content, contentType: "text/xml", encoding, statusCode);
    }

    public static IResult ForwardToUpstream(this IResultExtensions _)
    {
        return new ForwardResult(httpClient: null);
    }

    public static IResult ForwardToUpstream(this IResultExtensions _, HttpClient httpClient)
    {
        if (httpClient is null)
            throw new ArgumentNullException(nameof(httpClient));

        return new ForwardResult(httpClient);
    }
}
