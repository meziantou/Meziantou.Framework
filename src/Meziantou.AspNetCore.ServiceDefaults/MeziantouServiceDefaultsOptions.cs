using System.Text.Json;

namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouServiceDefaultsOptions
{
    public MeziantouOpenApiConfiguration OpenApi { get; } = new();
    public MeziantouOpenTelemetryConfiguration OpenTelemetry { get; } = new();
    public Action<JsonSerializerOptions>? ConfigureJsonOptions { get; set; }

}
