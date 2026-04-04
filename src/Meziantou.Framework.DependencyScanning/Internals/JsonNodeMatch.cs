using System.Text.Json.Nodes;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal readonly record struct JsonNodeMatch(JsonNode? Node, string Path, LineInfo LineInfo);
