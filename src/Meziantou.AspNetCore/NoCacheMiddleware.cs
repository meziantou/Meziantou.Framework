using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore;

public sealed class NoCacheMiddleware(RequestDelegate next)
{
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
