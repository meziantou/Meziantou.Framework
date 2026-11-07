using Meziantou.AspNetCore.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meziantou.AspNetCore.Samples;

/// <summary>The examples of src/Meziantou.AspNetCore.ServiceDefaults/readme.md.</summary>
public static class ServiceDefaultsExamples
{
    public static void BasicSetup(string[] args)
    {
        #region ServiceDefaults_BasicSetup
        var builder = WebApplication.CreateBuilder(args);

        // Add Meziantou service defaults
        builder.UseMeziantouConventions();

        var app = builder.Build();

        // Map default endpoints (health checks, OpenAPI, etc.)
        app.MapMeziantouDefaultEndpoints();

        app.MapGet("/", () => "Hello World!");

        app.Run();
        #endregion
    }
}
