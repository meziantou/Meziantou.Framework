using Meziantou.Framework.Internals;
using Microsoft.AspNetCore.Http;

namespace Meziantou.Framework;

/// <summary>
/// Extension methods for creating <see cref="IResult"/> instances with various content types.
/// </summary>
/// <summary>
/// Extension methods for creating <see cref="IResult"/> instances with various content types.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Creates a result that returns raw JSON content.</summary>
    /// <param name="_">The result extensions.</param>
    /// <param name="content">The JSON content to return.</param>
    /// <param name="encoding">The encoding to use for the content.</param>
    /// <returns>An <see cref="IResult"/> that writes the JSON content to the response.</returns>
    public static IResult RawJson(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Json)] string content, Encoding? encoding)
    {
        return RawJson(_, content, encoding, statusCode: null);
    }

    /// <summary>Creates a result that returns raw JSON content with an optional status code.</summary>
    /// <param name="_">The result extensions.</param>
    /// <param name="content">The JSON content to return.</param>
    /// <param name="encoding">The encoding to use for the content.</param>
    /// <param name="statusCode">The HTTP status code to return.</param>
    /// <returns>An <see cref="IResult"/> that writes the JSON content to the response.</returns>
    public static IResult RawJson(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Json)] string content, Encoding? encoding = null, int? statusCode = null)
    {
        return Results.Text(content, contentType: "application/json", encoding, statusCode);
    }

    /// <summary>Creates a result that returns raw XML content.</summary>
    /// <param name="_">The result extensions.</param>
    /// <param name="content">The XML content to return.</param>
    /// <param name="encoding">The encoding to use for the content.</param>
    /// <returns>An <see cref="IResult"/> that writes the XML content to the response.</returns>
    public static IResult RawXml(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Xml)] string content, Encoding? encoding)
    {
        return RawXml(_, content, encoding, statusCode: null);
    }

    /// <summary>Creates a result that returns raw XML content with an optional status code.</summary>
    /// <param name="_">The result extensions.</param>
    /// <param name="content">The XML content to return.</param>
    /// <param name="encoding">The encoding to use for the content.</param>
    /// <param name="statusCode">The HTTP status code to return.</param>
    /// <returns>An <see cref="IResult"/> that writes the XML content to the response.</returns>
    public static IResult RawXml(this IResultExtensions _, [StringSyntax(StringSyntaxAttribute.Xml)] string content, Encoding? encoding = null, int? statusCode = null)
    {
        return Results.Text(content, contentType: "text/xml", encoding, statusCode);
    }

    /// <summary>Creates a result that forwards the request to the actual upstream server.</summary>
    /// <param name="_">The result extensions.</param>
    /// <returns>An <see cref="IResult"/> that forwards the request.</returns>
    public static IResult ForwardToUpstream(this IResultExtensions _)
    {
        return new ForwardResult(httpClient: null);
    }

    /// <summary>Creates a result that forwards the request to the actual upstream server using the specified <see cref="HttpClient"/>.</summary>
    /// <param name="_">The result extensions.</param>
    /// <param name="httpClient">The <see cref="HttpClient"/> to use for forwarding the request.</param>
    /// <returns>An <see cref="IResult"/> that forwards the request.</returns>
    public static IResult ForwardToUpstream(this IResultExtensions _, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        return new ForwardResult(httpClient);
    }
}
