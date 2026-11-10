namespace Meziantou.Framework.Diagrams;

/// <summary>A link of a Wardley map: a dependency of a component on another one, or a flow between them.</summary>
public sealed class WardleyLink
{
    private string _source;
    private string _target;

    /// <summary>Initializes a new instance of the <see cref="WardleyLink"/> class.</summary>
    /// <param name="source">The name of the component that depends on the target.</param>
    /// <param name="target">The name of the component the source depends on.</param>
    public WardleyLink(string source, string target)
    {
        _source = WardleyCoordinates.CheckName(source, nameof(source));
        _target = WardleyCoordinates.CheckName(target, nameof(target));
    }

    /// <summary>Gets or sets the name of the source: a component, an anchor, or a component of a pipeline.</summary>
    public string Source
    {
        get => _source;
        set => _source = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets the name of the target: a component, an anchor, or a component of a pipeline.</summary>
    public string Target
    {
        get => _target;
        set => _target = WardleyCoordinates.CheckName(value, nameof(value));
    }

    /// <summary>Gets or sets a value that indicates whether the line is dashed (Mermaid <c>-.-&gt;</c>).</summary>
    public bool IsDashed { get; set; }

    /// <summary>Gets or sets the direction of the flow, drawn as arrowheads.</summary>
    public WardleyFlow Flow { get; set; }

    /// <summary>Gets or sets the text drawn along the line, or <see langword="null"/>.</summary>
    public string? Label { get; set; }

    /// <summary>Returns the source and the target of the link.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Source + " -> " + Target;
}
