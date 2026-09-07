using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

[JsonSerializable(typeof(HarDocument))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
internal sealed partial class HarSerializerContext : JsonSerializerContext;
