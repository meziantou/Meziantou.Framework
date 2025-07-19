using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.Framework.Internals;

internal sealed class ForwardResult(HttpClient? httpClient) : IResult
{
    public Task ExecuteAsync(HttpContext context)
    {
        return ExecuteAsyncCore(context, httpClient);
    }

    public static async Task ExecuteAsyncCore(HttpContext context, HttpClient? httpClient = null)
    {
        var localHttpClient = httpClient ?? context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        var request = context.Request;

#if NET8_0_OR_GREATER
        var method = HttpMethod.Parse(context.Request.Method);
#else
        var method = new HttpMethod(context.Request.Method);
#endif
        var url = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
        using var requestMessage = new HttpRequestMessage(method, url)
        {
            Content = new StreamContent(context.Request.Body),
        };

        foreach (var header in request.Headers)
        {
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.AsEnumerable());
        }

        using var response = await localHttpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
    }
}
