using System.Text.Json.Serialization;

namespace Meziantou.Framework.TemporaryContainers.Internals;

[JsonSerializable(typeof(AppleInspectResult[]))]
[JsonSerializable(typeof(AppleImageDto[]))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
internal sealed partial class AppleInspectJsonContext : JsonSerializerContext;
