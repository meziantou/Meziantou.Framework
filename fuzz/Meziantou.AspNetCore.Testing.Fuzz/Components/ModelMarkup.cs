namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

internal sealed class ModelMarkup(string html) : ModelNode
{
    public string Html { get; set; } = html;
}
