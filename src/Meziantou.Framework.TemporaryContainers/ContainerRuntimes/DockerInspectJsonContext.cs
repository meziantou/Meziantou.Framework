using System.Text.Json.Serialization;

namespace Meziantou.Framework.TemporaryContainers.Internals;

[JsonSerializable(typeof(DockerInspectResult[]))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class DockerInspectJsonContext : JsonSerializerContext;
