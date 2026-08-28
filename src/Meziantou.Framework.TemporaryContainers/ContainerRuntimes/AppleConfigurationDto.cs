using System.Text.Json;

namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class AppleConfigurationDto
{
    public string? Id { get; set; }
    public string? Hostname { get; set; }
    public JsonElement Image { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public List<ApplePublishedPortDto>? PublishedPorts { get; set; }
}
