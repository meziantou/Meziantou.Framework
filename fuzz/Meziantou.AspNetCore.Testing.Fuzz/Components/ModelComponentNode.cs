namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

/// <summary>A child <see cref="ModelComponent"/> that renders the children.</summary>
internal sealed class ModelComponentNode(int? key, bool inSvg) : ModelContainer(inSvg)
{
    public int? Key { get; } = key;
}
