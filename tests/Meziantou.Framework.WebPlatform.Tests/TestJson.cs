using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Tests;

/// <summary>
/// The JSON files of the test suites, parsed once: each data-driven test reads one test of a file, and parsing the whole file
/// for each test is quadratic (e.g. the 7,000 html5lib tokenizer tests).
/// </summary>
internal static class TestJson
{
    private static readonly ConcurrentDictionary<string, Lazy<JsonDocument>> Documents = new(StringComparer.Ordinal);

    /// <summary>Returns the root element of a file. The document is shared by the tests (a JsonElement can be read concurrently).</summary>
    public static JsonElement Load(string path) => Documents.GetOrAdd(path, static path => new Lazy<JsonDocument>(() => JsonDocument.Parse(File.ReadAllText(path)))).Value.RootElement;

    /// <summary>Returns a mutable copy of an element, or null for the JSON null.</summary>
    public static JsonNode? ToNode(JsonElement element) => JsonNode.Parse(element.GetRawText());
}
