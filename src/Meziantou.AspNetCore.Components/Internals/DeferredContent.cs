using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components.Internals;

/// <summary>
/// Renders its content after the components that precede it in the render tree of its parent: it is a child component,
/// so the renderer renders it after the sibling components queued before it (the columns of a <see cref="DataGrid{TRowData}"/>).
/// </summary>
internal sealed class DeferredContent : ComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);
}
