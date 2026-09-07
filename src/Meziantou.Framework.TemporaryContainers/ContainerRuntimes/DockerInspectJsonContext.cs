using System.Text.Json.Serialization;

namespace Meziantou.Framework.TemporaryContainers.Internals;

[JsonSerializable(typeof(DockerInspectResult[]))]
[JsonSerializable(typeof(DockerVolumeInspectResult[]))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class DockerInspectJsonContext : JsonSerializerContext;
