using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore;

/// <summary>
/// Ensures responses are marked as non-cacheable when no Cache-Control header is already set.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class NoCacheMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Processes the request and adds a default non-cacheable Cache-Control header to the response when none is set.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the middleware has finished processing.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (context.Response.Headers.CacheControl.Count is 0)
            {
                context.Response.Headers.CacheControl = "no-cache,no-store,must-revalidate";
            }

            return Task.CompletedTask;
        });

        await next(context);
    }
}
