namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

internal sealed class ModelText(string text) : ModelNode
{
    public string Text { get; set; } = text;
}
