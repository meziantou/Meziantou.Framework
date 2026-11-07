using System.Reflection;
using System.Text.Json;

namespace Meziantou.Framework.Fuzzing;

/// <summary>Reads the test corpora that the fuzz projects embed as seeds.</summary>
public static class FuzzResources
{
    public static Stream Open(Assembly assembly, string name)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"The resource '{name}' does not exist in {assembly.GetName().Name}.");
    }

    public static string ReadText(Assembly assembly, string name)
    {
        using var reader = new StreamReader(Open(assembly, name));
        return reader.ReadToEnd();
    }

    public static JsonDocument ReadJson(Assembly assembly, string name)
    {
        using var stream = Open(assembly, name);
        return JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
    }
}
