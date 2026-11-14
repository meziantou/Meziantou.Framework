using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>Where a root component is rendered. The default is the <c>body</c> of the page (a new <c>div</c> for the next root components).</summary>
public sealed class RenderOptions
{
    /// <summary>Gets the element that hosts the component: its children are replaced by the output of the component.</summary>
    public Element? Container { get; init; }

    /// <summary>Gets the CSS selector of the element that hosts the component (for instance <c>#app</c> of the host page).</summary>
    public string? ContainerSelector { get; init; }
}
