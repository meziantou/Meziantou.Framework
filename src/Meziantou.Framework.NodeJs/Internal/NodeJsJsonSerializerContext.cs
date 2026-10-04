using System.Text.Json.Serialization;

namespace Meziantou.Framework.NodeJs.Internal;

[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(JSValue))]
[JsonSerializable(typeof(JSReference))]
internal sealed partial class NodeJsJsonSerializerContext : JsonSerializerContext;
