using Meziantou.AspNetCore.Tests;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Declared in <c>Microsoft.AspNetCore.Builder</c> on purpose: that is the convention third-party <c>UseXxx</c>
/// helpers follow, and it is the namespace the middleware name walk is allowed to inspect. The captured
/// <see cref="ThrowingCollection"/> is therefore reachable by the walk.
/// </summary>
internal static class ThrowingCollectionMiddlewareExtensions
{
    public static IApplicationBuilder UseThrowingCollectionMiddleware(this IApplicationBuilder app)
    {
        var hostile = new ThrowingCollection();
        return app.Use(next => context =>
        {
            _ = hostile;
            return next(context);
        });
    }
}
