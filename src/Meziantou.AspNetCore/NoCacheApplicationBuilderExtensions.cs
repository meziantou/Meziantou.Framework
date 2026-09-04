using Microsoft.AspNetCore.Builder;

namespace Meziantou.AspNetCore;

/// <summary>Extension methods to register <see cref="NoCacheMiddleware"/>.</summary>
public static class NoCacheApplicationBuilderExtensions
{
    /// <summary>
    /// Adds a middleware that sets a default non-cacheable <c>Cache-Control</c> response header when the response
    /// does not define one.
    /// </summary>
    /// <remarks>
    /// Register this after any component that serves cacheable content, such as <c>UseStaticFiles</c>. See
    /// <see cref="NoCacheMiddleware"/> for the caching implications of the default value.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder, to allow chaining.</returns>
    public static IApplicationBuilder UseNoCache(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<NoCacheMiddleware>();
    }
}
