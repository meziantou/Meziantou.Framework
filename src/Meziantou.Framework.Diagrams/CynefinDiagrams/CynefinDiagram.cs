using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A diagram of the Cynefin framework: the complex, complicated, chaotic and clear domains as four quadrants separated by
/// wavy boundaries, the confusion domain in their center, items placed in the domains, and transitions between the
/// domains (Mermaid <c>cynefin-beta</c>). The domains are always at the same place: complex at the top left,
/// complicated at the top right, chaotic at the bottom left and clear at the bottom right.
/// </summary>
public sealed class CynefinDiagram : Diagram
{
    private double _width = 800;
    private double _height = 600;
    private double _boundaryAmplitude = 8;

    /// <summary>Initializes a new instance of the <see cref="CynefinDiagram"/> class, with the padding of Mermaid, 40 pixels.</summary>
    public CynefinDiagram()
    {
        Padding = 40;
    }

    /// <summary>Gets the items. The items of a domain are drawn from the top in their order; the confusion domain shows three of them, and the number of the others.</summary>
    public IList<CynefinItem> Items { get; } = new List<CynefinItem>();

    /// <summary>Gets the transitions between the domains.</summary>
    public IList<CynefinTransition> Transitions { get; } = new List<CynefinTransition>();

    /// <summary>Gets the colors and sizes that replace the ones of the theme (the <c>cynefin</c> theme variables of Mermaid).</summary>
    public CynefinTheme Colors { get; } = new();

    /// <summary>Gets or sets the width of the diagram, without the padding, in pixels. The default is 800.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 10000.</exception>
    public double Width { get => _width; set => _width = Check(value); }

    /// <summary>Gets or sets the height of the diagram, without the padding, in pixels. The default is 600.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 10000.</exception>
    public double Height { get => _height; set => _height = Check(value); }

    /// <summary>Gets or sets a value that indicates whether the decision model and the practices of each domain are written below its name. The default is <see langword="true"/>.</summary>
    public bool ShowDomainDescriptions { get; set; } = true;

    /// <summary>Gets or sets how much the boundaries between the domains wave, in pixels; 0 draws straight lines. The default is 8.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 50.</exception>
    public double BoundaryAmplitude
    {
        get => _boundaryAmplitude;
        set
        {
            if (!(value >= 0 && value <= 50))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The amplitude must be between 0 and 50.");

            _boundaryAmplitude = value;
        }
    }

    /// <summary>
    /// Gets or sets the seed of the waves of the boundaries: the same seed always draws the same waves. 0, the default,
    /// uses a fixed seed (Mermaid derives it from the identifier of the SVG element, so its waves change between pages).
    /// </summary>
    public int Seed { get; set; }

    /// <summary>Adds an item to a domain.</summary>
    /// <param name="domain">The domain.</param>
    /// <param name="label">The text of the item.</param>
    /// <returns>The item.</returns>
    public CynefinItem AddItem(CynefinDomain domain, DiagramText label)
    {
        var item = new CynefinItem(domain, label);
        Items.Add(item);
        return item;
    }

    /// <summary>Adds a transition between two domains.</summary>
    /// <param name="from">The domain the movement starts from.</param>
    /// <param name="to">The domain the movement goes to.</param>
    /// <param name="label">The text drawn on the arrow, or <see langword="null"/> for none.</param>
    /// <returns>The transition.</returns>
    public CynefinTransition AddTransition(CynefinDomain from, CynefinDomain to, DiagramText? label = null)
    {
        var transition = new CynefinTransition(from, to, label);
        Transitions.Add(transition);
        return transition;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => CynefinDiagramRenderer.Render(this, context, group);

    private static double Check(double value)
    {
        if (!(value >= 1 && value <= 10_000))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The size must be between 1 and 10000.");

        return value;
    }
}
