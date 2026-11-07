using System.Text.Json.Serialization;

namespace Meziantou.Framework.NodeJs.Samples;

[JsonSerializable(typeof(Person))]
[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
internal sealed partial class MyJsonContext : JsonSerializerContext
{
}
