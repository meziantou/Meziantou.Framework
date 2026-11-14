using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>The root component of a fragment rendered by <see cref="ComponentTestHost.RenderAsync(RenderFragment, RenderOptions?, CancellationToken)"/>.</summary>
public sealed class FragmentContainer : IComponent
{
    private RenderHandle _renderHandle;

    /// <summary>Gets or sets the rendered fragment.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <inheritdoc />
    public void Attach(RenderHandle renderHandle) => _renderHandle = renderHandle;

    /// <inheritdoc />
    public Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        _renderHandle.Render(ChildContent ?? (_ => { }));
        return Task.CompletedTask;
    }
}
