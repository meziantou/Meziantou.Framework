using System.Globalization;
using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A Wardley map: the components of a value chain placed by their visibility to the user (vertically) and their
/// evolution, from genesis to commodity (horizontally), with their dependencies, their movements and the forces on them
/// (Mermaid <c>wardley-beta</c>). The coordinates are between 0 and 1.
/// </summary>
public sealed class WardleyDiagram : Diagram
{
    private double _width = 900;
    private double _height = 600;
    private double _mapPadding = 48;
    private double _nodeRadius = 6;
    private double _nodeLabelOffset = 8;
    private double _axisFontSize = 12;
    private double _labelFontSize = 10;

    /// <summary>Initializes a new instance of the <see cref="WardleyDiagram"/> class, without padding: the map is drawn on a canvas of <see cref="Width"/> by <see cref="Height"/> pixels, as in Mermaid.</summary>
    public WardleyDiagram()
    {
        Padding = 0;
    }

    /// <summary>Gets or sets the width of the canvas, in pixels. The default is 900.</summary>
    public double Width
    {
        get => _width;
        set => _width = CheckRange(value, 0, 100_000);
    }

    /// <summary>Gets or sets the height of the canvas, in pixels. The default is 600.</summary>
    public double Height
    {
        get => _height;
        set => _height = CheckRange(value, 0, 100_000);
    }

    /// <summary>Gets or sets the space between the edges of the canvas and the axes, in pixels. The default is 48.</summary>
    public double MapPadding
    {
        get => _mapPadding;
        set => _mapPadding = CheckRange(value, 0, 10_000);
    }

    /// <summary>Gets or sets the radius of the circles of the components, in pixels. The default is 6.</summary>
    public double NodeRadius
    {
        get => _nodeRadius;
        set => _nodeRadius = CheckRange(value, 0, 1000);
    }

    /// <summary>Gets or sets the distance between a component and its label, in pixels. The default is 8.</summary>
    public double NodeLabelOffset
    {
        get => _nodeLabelOffset;
        set => _nodeLabelOffset = CheckRange(value, 0, 1000);
    }

    /// <summary>Gets or sets the size of the font of the names of the axes, in pixels. The stages are 2 pixels smaller. The default is 12.</summary>
    public double AxisFontSize
    {
        get => _axisFontSize;
        set => _axisFontSize = CheckRange(value, 1, 1000);
    }

    /// <summary>Gets or sets the size of the font of the labels of the components and the links, in pixels. The default is 10.</summary>
    public double LabelFontSize
    {
        get => _labelFontSize;
        set => _labelFontSize = CheckRange(value, 1, 1000);
    }

    /// <summary>Gets or sets a value that indicates whether a grid divides the map in quarters.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>
    /// Gets the stages of the evolution axis, from left to right (Mermaid <c>evolution</c>). When the list is empty, the
    /// stages are Genesis, Custom Built, Product and Commodity. Mermaid needs at least two stages.
    /// </summary>
    public IList<WardleyStage> Stages { get; } = new List<WardleyStage>();

    /// <summary>Gets the anchors: the users or the customers whose needs the value chain serves.</summary>
    public IList<WardleyAnchor> Anchors { get; } = new List<WardleyAnchor>();

    /// <summary>Gets the components. A component that has the name of an anchor or of a previous component replaces its position.</summary>
    public IList<WardleyComponent> Components { get; } = new List<WardleyComponent>();

    /// <summary>Gets the pipelines: the variants of a component at different stages of evolution.</summary>
    public IList<WardleyPipeline> Pipelines { get; } = new List<WardleyPipeline>();

    /// <summary>Gets the links between the components. A link to a name that is not on the map is not drawn.</summary>
    public IList<WardleyLink> Links { get; } = new List<WardleyLink>();

    /// <summary>Gets the movements of the components toward a later evolution. The last movement of a component replaces the previous ones.</summary>
    public IList<WardleyEvolve> Evolves { get; } = new List<WardleyEvolve>();

    /// <summary>Gets the notes.</summary>
    public IList<WardleyNote> Notes { get; } = new List<WardleyNote>();

    /// <summary>Gets the numbered annotations.</summary>
    public IList<WardleyAnnotation> Annotations { get; } = new List<WardleyAnnotation>();

    /// <summary>
    /// Gets or sets the position of the top-left corner of the box that lists the texts of the annotations (Mermaid
    /// <c>annotations [visibility, evolution]</c>), or <see langword="null"/> to draw no box. The box stays inside the map.
    /// </summary>
    public WardleyPosition? AnnotationsPosition { get; set; }

    /// <summary>Gets the accelerators: the forces that speed up the evolution, drawn as arrows pointing right.</summary>
    public IList<WardleyAccelerator> Accelerators { get; } = new List<WardleyAccelerator>();

    /// <summary>Gets the deaccelerators: the forces that slow down the evolution, drawn as arrows pointing left.</summary>
    public IList<WardleyAccelerator> Deaccelerators { get; } = new List<WardleyAccelerator>();

    /// <summary>Adds an anchor.</summary>
    /// <param name="name">The name of the anchor.</param>
    /// <param name="visibility">The visibility, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution, between 0 (left) and 1 (right).</param>
    /// <returns>The anchor.</returns>
    public WardleyAnchor AddAnchor(string name, double visibility, double evolution)
    {
        var anchor = new WardleyAnchor(name, visibility, evolution);
        Anchors.Add(anchor);
        return anchor;
    }

    /// <summary>Adds a component.</summary>
    /// <param name="name">The name of the component.</param>
    /// <param name="visibility">The visibility, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution, between 0 (left) and 1 (right).</param>
    /// <returns>The component.</returns>
    public WardleyComponent AddComponent(string name, double visibility, double evolution)
    {
        var component = new WardleyComponent(name, visibility, evolution);
        Components.Add(component);
        return component;
    }

    /// <summary>Adds a pipeline to a component.</summary>
    /// <param name="parent">The name of the parent component.</param>
    /// <returns>The pipeline.</returns>
    public WardleyPipeline AddPipeline(string parent)
    {
        var pipeline = new WardleyPipeline(parent);
        Pipelines.Add(pipeline);
        return pipeline;
    }

    /// <summary>Adds a link.</summary>
    /// <param name="source">The name of the component that depends on the target.</param>
    /// <param name="target">The name of the component the source depends on.</param>
    /// <returns>The link.</returns>
    public WardleyLink AddLink(string source, string target)
    {
        var link = new WardleyLink(source, target);
        Links.Add(link);
        return link;
    }

    /// <summary>Adds a movement of a component toward a later evolution.</summary>
    /// <param name="component">The name of the component.</param>
    /// <param name="evolution">The evolution it moves to, between 0 (left) and 1 (right).</param>
    /// <returns>The movement.</returns>
    public WardleyEvolve AddEvolve(string component, double evolution)
    {
        var evolve = new WardleyEvolve(component, evolution);
        Evolves.Add(evolve);
        return evolve;
    }

    /// <summary>Adds a note.</summary>
    /// <param name="text">The text.</param>
    /// <param name="visibility">The visibility of its start, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution of its start, between 0 (left) and 1 (right).</param>
    /// <returns>The note.</returns>
    public WardleyNote AddNote(string text, double visibility, double evolution)
    {
        var note = new WardleyNote(text, visibility, evolution);
        Notes.Add(note);
        return note;
    }

    /// <summary>Adds a numbered annotation.</summary>
    /// <param name="number">The number.</param>
    /// <param name="text">The text, listed in the box of the annotations.</param>
    /// <param name="visibility">The visibility of the circle, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution of the circle, between 0 (left) and 1 (right).</param>
    /// <returns>The annotation.</returns>
    public WardleyAnnotation AddAnnotation(int number, string text, double visibility, double evolution)
    {
        var annotation = new WardleyAnnotation(number, text, visibility, evolution);
        Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Adds an accelerator: a force that speeds up the evolution.</summary>
    /// <param name="name">The name.</param>
    /// <param name="visibility">The visibility of the arrow, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution of the left end of the arrow, between 0 (left) and 1 (right).</param>
    /// <returns>The accelerator.</returns>
    public WardleyAccelerator AddAccelerator(string name, double visibility, double evolution)
    {
        var accelerator = new WardleyAccelerator(name, visibility, evolution);
        Accelerators.Add(accelerator);
        return accelerator;
    }

    /// <summary>Adds a deaccelerator: a force that slows down the evolution.</summary>
    /// <param name="name">The name.</param>
    /// <param name="visibility">The visibility of the arrow, between 0 (bottom) and 1 (top).</param>
    /// <param name="evolution">The evolution of the left end of the arrow, between 0 (left) and 1 (right).</param>
    /// <returns>The deaccelerator.</returns>
    public WardleyAccelerator AddDeaccelerator(string name, double visibility, double evolution)
    {
        var deaccelerator = new WardleyAccelerator(name, visibility, evolution);
        Deaccelerators.Add(deaccelerator);
        return deaccelerator;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => WardleyDiagramRenderer.Render(this, context, group);

    private static double CheckRange(double value, double minimum, double maximum)
    {
        if (!(value >= minimum && value <= maximum))
            throw new ArgumentOutOfRangeException(nameof(value), value, string.Create(CultureInfo.InvariantCulture, $"The value must be between {minimum} and {maximum}."));

        return value;
    }
}
