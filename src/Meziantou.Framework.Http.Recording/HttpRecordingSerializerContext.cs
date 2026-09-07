using System.Text.Json.Serialization;

namespace Meziantou.Framework.Http.Recording;

[JsonSerializable(typeof(List<HttpRecordingEntry>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    // The store validates the entries itself so it can name the file and the index of the faulty entry. Honoring the
    // nullable annotations would let the deserializer reject a null value first with a message blaming a truncated
    // file, and the diagnostic would depend on whether the host application enabled the switch.
    RespectNullableAnnotations = false)]
internal sealed partial class HttpRecordingSerializerContext : JsonSerializerContext;
