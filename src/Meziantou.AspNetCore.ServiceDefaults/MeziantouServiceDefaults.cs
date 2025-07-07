using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Meziantou.AspNetCore.ServiceDefaults;

public static class MeziantouServiceDefaults
{
    public static TBuilder UseMeziantouConventions<TBuilder>(this TBuilder builder, Action<MeziantouServiceDefaultsOptions>? configure = null) where TBuilder : IHostApplicationBuilder
    {
        var options = new MeziantouServiceDefaultsOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);
        if (options.AntiForgery.Enabled)
        {
            builder.Services.AddAntiforgery();
        }

        builder.ConfigureOpenTelemetry(options);
        builder.AddDefaultHealthChecks();

        if (options.OpenApi.Enabled)
        {
            builder.Services.AddOpenApi(options.OpenApi.ConfigureOpenApi ?? (_ => { }));
        }

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => ConfigureJsonOptions(options.JsonSerializerOptions));
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => ConfigureJsonOptions(options.SerializerOptions));
        return builder;
    }

    private static void ConfigureJsonOptions(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
    }

    private static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder, MeziantouServiceDefaultsOptions options) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            options.OpenTelemetry.ConfigureLogging?.Invoke(logging);
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(x =>
            {
                var name = builder.Environment.ApplicationName;
                var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
                x.AddService(name, serviceVersion: version);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                options.OpenTelemetry.ConfigureMetrics?.Invoke(metrics);
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.EnableAspNetCoreSignalRSupport = true;
                        options.Filter = context =>
                        {
                            if (context.Request.Path == "/health" || context.Request.Path == "/alive")
                                return false;

                            return true;
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource("Meziantou.*");

                options.OpenTelemetry.ConfigureTracing?.Invoke(tracing);
            });

        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    private static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static void MapMeziantouDefaultEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<MeziantouServiceDefaultsOptions>();

        var environment = app.Services.GetRequiredService<IWebHostEnvironment>();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        if (options.AntiForgery.Enabled)
        {
            app.UseAntiforgery();
        }

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        app.MapStaticAssets();

        if (options.OpenApi.Enabled)
        {
            app.MapOpenApi(options.OpenApi.RoutePattern).CacheOutput();
        }
    }
}
