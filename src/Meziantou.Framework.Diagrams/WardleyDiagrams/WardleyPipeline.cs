namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A pipeline of a Wardley map: the variants of a component at different stages of evolution, drawn in a box at the
/// visibility of the component, which moves above the box as a square (Mermaid <c>pipeline Parent { ... }</c>).
/// </summary>
public sealed class WardleyPipeline
{
    private string _parent;

    /// <summary>Initializes a new instance of the <see cref="WardleyPipeline"/> class.</summary>
    /// <param name="parent">The name of the parent component.</param>
    public WardleyPipeline(string parent)
    {
        _parent = WardleyCoordinates.CheckName(parent, nameof(parent));
    }

    /// <summary>Gets or sets the name of the parent component. Mermaid needs a component or an anchor of this name.</summary>
    public string Parent
    {
        get => _parent;
        set => _parent = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets the components of the pipeline. Mermaid needs at least one.</summary>
    public IList<WardleyPipelineComponent> Components { get; } = new List<WardleyPipelineComponent>();

    /// <summary>Adds a component to the pipeline.</summary>
    /// <param name="name">The name of the component.</param>
    /// <param name="evolution">The evolution, between 0 (left) and 1 (right).</param>
    /// <returns>The component.</returns>
    public WardleyPipelineComponent AddComponent(string name, double evolution)
    {
        var component = new WardleyPipelineComponent(name, evolution);
        Components.Add(component);
        return component;
    }

    /// <summary>Returns the name of the parent component.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Parent;
}
