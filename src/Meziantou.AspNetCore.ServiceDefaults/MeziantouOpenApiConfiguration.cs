using Microsoft.AspNetCore.OpenApi;

namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouOpenApiConfiguration
{
    public bool Enabled { get; set; } = true;
    public Action<OpenApiOptions>? ConfigureOpenApi { get; set; }
    [StringSyntax("Route")]
    public string RoutePattern { get; set; } = "/openapi/{documentName}.json";
}
