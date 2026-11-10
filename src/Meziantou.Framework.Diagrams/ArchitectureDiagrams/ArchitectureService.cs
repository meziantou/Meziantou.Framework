namespace Meziantou.Framework.Diagrams;

/// <summary>A service of an architecture diagram: an icon with its title below.</summary>
public sealed class ArchitectureService : ArchitectureNode
{
    /// <summary>Initializes a new instance of the <see cref="ArchitectureService"/> class.</summary>
    /// <param name="id">The identifier, unique in the diagram.</param>
    public ArchitectureService(string id)
        : base(id)
    {
    }

    /// <summary>Gets or sets the title shown below the icon, or <see langword="null"/> for none.</summary>
    public DiagramText? Title { get; set; }

    /// <summary>
    /// Gets or sets the name of the icon: <c>cloud</c>, <c>database</c>, <c>disk</c>, <c>internet</c> or <c>server</c>.
    /// Another name, such as the icon of an icon pack of Mermaid (<c>logos:aws-s3</c>), is drawn as a question mark.
    /// <see langword="null"/> draws an empty square.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>Gets or sets a text drawn in the square instead of an icon, or <see langword="null"/> for none.</summary>
    public string? IconText { get; set; }
}
