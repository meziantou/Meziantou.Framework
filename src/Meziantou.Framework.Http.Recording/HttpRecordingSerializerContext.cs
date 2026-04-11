using System.Text.Json.Serialization;

namespace Meziantou.Framework.Http.Recording;

[JsonSerializable(typeof(List<HttpRecordingEntry>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class HttpRecordingSerializerContext : JsonSerializerContext;
