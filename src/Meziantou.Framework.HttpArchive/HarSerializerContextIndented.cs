using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

[JsonSerializable(typeof(HarDocument))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class HarSerializerContextIndented : JsonSerializerContext;
