using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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

        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            options.AddServerHeader = false;
        });

        builder.Services.AddSingleton<IStartupFilter>(new ValidationStartupFilter());
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
            http.ConfigureHttpClient((serviceProvider, client) =>
            {
                var hostEnvironment = serviceProvider.GetRequiredService<IHostEnvironment>();
                client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue(hostEnvironment.ApplicationName, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"));
            });
        });

        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(jsonOptions => ConfigureJsonOptions(jsonOptions.JsonSerializerOptions, options));
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(jsonOptions => ConfigureJsonOptions(jsonOptions.SerializerOptions, options));
        return builder;
    }

    private static void ConfigureJsonOptions(JsonSerializerOptions jsonOptions, MeziantouServiceDefaultsOptions options)
    {
        jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        jsonOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        jsonOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        jsonOptions.RespectNullableAnnotations = true;
        jsonOptions.RespectRequiredConstructorParameters = true;
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        options.ConfigureJsonOptions?.Invoke(jsonOptions);
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
        options.MapCalled = true;

        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = options.ForwardedHeaders.ForwardedHeaders });
        if (options.Https.Enabled)
        {
            app.UseHttpsRedirection();
        }

        var environment = app.Services.GetRequiredService<IWebHostEnvironment>();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);

            if (options.Https.Enabled && options.Https.HstsEnabled)
            {
                app.UseHsts();
            }
        }

        if (options.AntiForgery.Enabled)
        {
            app.UseAntiforgery();
        }

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        if (options.StaticAssets.Enabled)
        {
            // Check if the static web assets manifest exists
            var staticAssetsManifestPath = $"{environment.ApplicationName}.staticwebassets.endpoints.json";

            staticAssetsManifestPath = !Path.IsPathRooted(staticAssetsManifestPath) ?
                Path.Combine(AppContext.BaseDirectory, staticAssetsManifestPath) :
                staticAssetsManifestPath;

            if (File.Exists(staticAssetsManifestPath))
            {
                app.MapStaticAssets();
            }
        }

        if (options.OpenApi.Enabled)
        {
            app.MapOpenApi(options.OpenApi.RoutePattern).CacheOutput();
        }
    }
}
