using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

/// <summary>Renders a tree of <see cref="ModelNode"/>.</summary>
internal sealed class ModelComponent : ComponentBase
{
    [Parameter]
    public List<ModelNode>? Nodes { get; set; }

    public void Refresh() => StateHasChanged();

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Render(builder, Nodes ?? []);

    private static void Render(RenderTreeBuilder builder, List<ModelNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ModelElement element:
                    builder.OpenElement(10, element.Tag);
                    if (element.Key is { } elementKey)
                    {
                        builder.SetKey(elementKey);
                    }

                    foreach (var (name, value) in element.Attributes)
                    {
                        builder.AddAttribute(11, name, value);
                    }

                    Render(builder, element.Children);
                    builder.CloseElement();
                    break;

                case ModelText text:
                    builder.AddContent(20, text.Text);
                    break;

                case ModelMarkup markup:
                    builder.AddMarkupContent(30, markup.Html);
                    break;

                case ModelComponentNode component:
                    builder.OpenComponent<ModelComponent>(40);
                    if (component.Key is { } componentKey)
                    {
                        builder.SetKey(componentKey);
                    }

                    builder.AddComponentParameter(41, nameof(Nodes), component.Children);
                    builder.CloseComponent();
                    break;

                case ModelRegion region:
                    builder.OpenRegion(50);
                    Render(builder, region.Children);
                    builder.CloseRegion();
                    break;
            }
        }
    }
}
