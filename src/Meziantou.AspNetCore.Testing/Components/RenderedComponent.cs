using Meziantou.AspNetCore.Testing.Internals;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace Meziantou.AspNetCore.Testing.Components;

/// <summary>A component rendered by a <see cref="ComponentTestHost"/>, and the DOM nodes it renders. Its locators search the elements rendered by the component.</summary>
/// <typeparam name="TComponent">The type of the component.</typeparam>
public sealed class RenderedComponent<TComponent> : LocatorScope
    where TComponent : IComponent
{
    private readonly bool _isRootComponent;

    internal RenderedComponent(ComponentTestHost host, int componentId, TComponent instance, bool isRootComponent = true)
    {
        Host = host;
        ComponentId = componentId;
        Instance = instance;
        _isRootComponent = isRootComponent;
    }

    /// <summary>Gets the test host that renders the component.</summary>
    public ComponentTestHost Host { get; }

    /// <summary>Gets the instance of the component.</summary>
    public TComponent Instance { get; }

    /// <summary>Gets the identifier of the component in the renderer.</summary>
    public int ComponentId { get; }

    /// <summary>Gets the number of times the component was rendered.</summary>
    public int RenderCount => Host.Renderer.GetRenderCount(ComponentId);

    /// <summary>Gets the DOM nodes rendered by the component and its descendants, at the top level of its output.</summary>
    public IReadOnlyList<Node> Nodes
    {
        get
        {
            var nodes = new List<Node>();
            Host.Renderer.Applier.TryGetComponentDomNodes(ComponentId, nodes);
            return nodes;
        }
    }

    /// <summary>Gets the HTML rendered by the component, formatted for snapshots.</summary>
    public string Markup => GetMarkup();

    /// <summary>Gets the HTML rendered by the component, formatted for snapshots.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <returns>The HTML.</returns>
    public string GetMarkup(MarkupOptions? options = null) => DomSnapshot.GetMarkup(Nodes, options);

    /// <summary>Gets the accessibility tree of the elements rendered by the component, as a YAML snapshot (the format of the ARIA snapshots of Playwright).</summary>
    /// <returns>The snapshot.</returns>
    public string GetAccessibilitySnapshot() => DomSnapshot.GetAccessibilitySnapshot(Nodes);

    /// <summary>Gets the first element rendered by the component that matches a CSS selector, now (see <see cref="LocatorScope.Locator(string)"/> for a locator that waits).</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The element.</returns>
    /// <exception cref="ElementNotFoundException">No element matches the selector.</exception>
    public Element Find(string selector) => FindOrDefault(selector) ?? throw new ElementNotFoundException($"No element matches the selector '{selector}'");

    /// <summary>Gets the first element rendered by the component of a type that matches a CSS selector, now.</summary>
    /// <typeparam name="TElement">The type of the element.</typeparam>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The element.</returns>
    /// <exception cref="ElementNotFoundException">No element of type <typeparamref name="TElement"/> matches the selector.</exception>
    public TElement Find<TElement>(string selector)
        where TElement : Element
        => FindAll(selector).OfType<TElement>().FirstOrDefault() ?? throw new ElementNotFoundException($"No {typeof(TElement).Name} matches the selector '{selector}'");

    /// <summary>Gets the first element rendered by the component that matches a CSS selector now, or null.</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The element, or null.</returns>
    public Element? FindOrDefault(string selector) => FindAll(selector) is [var first, ..] ? first : null;

    /// <summary>Gets the elements rendered by the component that match a CSS selector now, in document order.</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <returns>The elements.</returns>
    public IReadOnlyList<Element> FindAll(string selector) => Locator(selector).GetElements();

    /// <summary>Takes a screenshot of the elements rendered by the component, encoded as PNG.</summary>
    /// <returns>The PNG image.</returns>
    /// <exception cref="InvalidOperationException">The component renders no element.</exception>
    public byte[] TakeScreenshotAsPng()
    {
        var elements = Nodes.OfType<Element>().ToArray();
        if (elements.Length == 0)
            throw new InvalidOperationException("The component renders no element");

        using (Host.Dispatcher.Enter())
        {
            if (elements.Length == 1)
                return Framework.WebPlatform.Imaging.ImagingScreenshotExtensions.TakeScreenshotAsPng(elements[0]);

            var rects = elements.Select(element => element.GetBoundingClientRect()).Where(rect => rect.Width > 0 && rect.Height > 0).ToArray();
            var left = rects.Min(rect => rect.Left);
            var top = rects.Min(rect => rect.Top);
            var right = rects.Max(rect => rect.Right);
            var bottom = rects.Max(rect => rect.Bottom);
            return Host.TakeScreenshotAsPng(new Framework.WebPlatform.Imaging.ScreenshotOptions { Clip = new(left, top, right - left, bottom - top) });
        }
    }

    /// <summary>Sets parameters of the component (the other parameters keep their values), and waits until the components are idle.</summary>
    /// <param name="parameters">Sets the parameters.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task completed once the components are idle.</returns>
    /// <exception cref="InvalidOperationException">The component is not a root component.</exception>
    public Task SetParametersAsync(Action<ComponentParameterCollectionBuilder<TComponent>> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!_isRootComponent)
            throw new InvalidOperationException("The parameters of a child component are set by its parent component");

        var builder = new ComponentParameterCollectionBuilder<TComponent>();
        parameters(builder);
        return Host.InvokeAsync(() => Host.Renderer.RenderRootAsync(ComponentId, builder.Build()), cancellationToken);
    }

    private protected override string TestIdAttributeCore => Host.TestIdAttribute;

    private protected override Testing.Locator CreateLocator(LocatorStep step) => Testing.Locator.Create(Host, () => Nodes, step);

    /// <summary>Gets the first descendant component of a type, in render order.</summary>
    /// <typeparam name="TChild">The type of the component.</typeparam>
    /// <returns>The component.</returns>
    /// <exception cref="ComponentNotFoundException">The component has no descendant of type <typeparamref name="TChild"/>.</exception>
    public RenderedComponent<TChild> FindComponent<TChild>()
        where TChild : IComponent
        => FindComponents<TChild>() is [var first, ..] ? first : throw new ComponentNotFoundException($"No component of type {typeof(TChild).Name} is rendered by {typeof(TComponent).Name}");

    /// <summary>Gets the descendant components of a type, in render order.</summary>
    /// <typeparam name="TChild">The type of the components.</typeparam>
    /// <returns>The components.</returns>
    public IReadOnlyList<RenderedComponent<TChild>> FindComponents<TChild>()
        where TChild : IComponent
    {
        var result = new List<RenderedComponent<TChild>>();
        using (Host.Dispatcher.Enter())
        {
            CollectComponents(ComponentId, result);
        }

        return result;
    }

    private void CollectComponents<TChild>(int componentId, List<RenderedComponent<TChild>> result)
        where TChild : IComponent
    {
        var frames = Host.Renderer.GetCurrentRenderTreeFrames(componentId);
        for (var i = 0; i < frames.Count; i++)
        {
            ref var frame = ref frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Component)
            {
                if (frame.Component is TChild child)
                {
                    result.Add(new RenderedComponent<TChild>(Host, frame.ComponentId, child, isRootComponent: false));
                }

                CollectComponents(frame.ComponentId, result);
            }
        }
    }
}
