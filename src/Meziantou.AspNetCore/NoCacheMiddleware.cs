using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Meziantou.AspNetCore;

/// <summary>
/// Ensures responses are marked as non-cacheable when no Cache-Control header is already set.
/// </summary>
/// <remarks>
/// <para>
/// Register this middleware <em>after</em> any component that serves cacheable content, such as
/// <c>UseStaticFiles</c>: the static file middleware sets <c>ETag</c> and <c>Last-Modified</c> but no
/// <c>Cache-Control</c>, so an earlier registration marks every static asset as non-cacheable.
/// </para>
/// <para>
/// Because the default includes <c>no-store</c>, responses defaulted by this middleware are not stored by
/// <c>UseResponseCaching</c>, <c>UseOutputCache</c> or a CDN. Responses that set their own
/// <c>Cache-Control</c> are never modified.
/// </para>
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class NoCacheMiddleware(RequestDelegate next)
{
    private const string NoCacheValue = "no-cache,no-store,must-revalidate";

    private static readonly Func<object, Task> OnStartingCallback = static state =>
    {
        var response = ((HttpContext)state).Response;
        if (StringValues.IsNullOrEmpty(response.Headers.CacheControl))
        {
            response.Headers.CacheControl = NoCacheValue;
        }

        return Task.CompletedTask;
    };

    /// <summary>
    /// Processes the request and adds a default non-cacheable Cache-Control header to the response when none is set.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the middleware has finished processing.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // OnStarting throws once the response has started. An upstream middleware may already have written and
        // flushed, in which case the header can no longer be defaulted and the request must not be failed for it.
        if (!context.Response.HasStarted)
        {
            context.Response.OnStarting(OnStartingCallback, context);
        }

        await next(context);
    }
}
