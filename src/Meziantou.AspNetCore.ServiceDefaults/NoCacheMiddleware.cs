using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore.ServiceDefaults;

[SuppressMessage("Usage", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated dynamically")]
internal sealed class NoCacheMiddleware(RequestDelegate next)
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
