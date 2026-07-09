using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class AppleConfigurationDto
{
    public string? Id { get; set; }
    public string? Hostname { get; set; }
    public JsonElement Image { get; set; }
}
