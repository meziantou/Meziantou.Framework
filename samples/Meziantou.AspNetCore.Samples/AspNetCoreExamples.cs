using Meziantou.AspNetCore;
using Meziantou.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore.Samples;

/// <summary>The examples of src/Meziantou.AspNetCore/readme.md.</summary>
public static class AspNetCoreExamples
{
    public static void MiddlewarePipelineDebugging(string[] args)
    {
        #region MiddlewarePipelineDebugging_Usage
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMiddlewarePipelineDebugging(); // must run before builder.Build()

        var app = builder.Build();

        app.UseRouting();
        app.MapGet("/hello", static () => "hello");

        // Maps GET /_debug/pipeline. By default it responds only in Development and returns 404 elsewhere.
        app.MapMiddlewarePipelineDebugEndpoint();

        app.MapGet("/pipeline.txt", () =>
        {
            var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
            return Results.Text(snapshot.ToString(), "text/plain");
        });

        app.Run();
        #endregion
    }

    public static void SecureDebugEndpoint(WebApplication app)
    {
        #region MiddlewarePipelineDebugging_SecureEndpoint
        app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: false)
           .RequireAuthorization("DiagnosticsPolicy");
        #endregion
    }

    public static void NoCache(string[] args)
    {
        #region NoCacheMiddleware_Usage
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.UseNoCache();

        app.MapGet("/", static () => "Hello World!");

        app.Run();
        #endregion
    }
}
