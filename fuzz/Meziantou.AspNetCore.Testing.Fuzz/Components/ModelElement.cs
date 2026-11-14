namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

internal sealed class ModelElement(string tag, int? key, bool inSvg) : ModelContainer(inSvg)
{
    public string Tag { get; } = tag;

    public int? Key { get; } = key;

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
}
