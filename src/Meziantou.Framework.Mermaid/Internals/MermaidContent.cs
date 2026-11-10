namespace Meziantou.Framework.Mermaid.Internals;

/// <summary>Reads and writes what the diagrams and the charts share: the title, the accessibility texts, the theme and the padding.</summary>
internal readonly struct MermaidContent(ISceneSource content)
{
    public string? Title
    {
        get => content switch
        {
            Diagram diagram => diagram.Title,
            MermaidChart chart => chart.Title,
            _ => throw Unsupported(content),
        };
        set
        {
            switch (content)
            {
                case Diagram diagram:
                    diagram.Title = value;
                    break;
                case MermaidChart chart:
                    chart.Title = value;
                    break;
                default:
                    throw Unsupported(content);
            }
        }
    }

    public string? AccessibleTitle
    {
        get => content switch
        {
            Diagram diagram => diagram.AccessibleTitle,
            MermaidChart chart => chart.AccessibleTitle,
            _ => throw Unsupported(content),
        };
        set
        {
            switch (content)
            {
                case Diagram diagram:
                    diagram.AccessibleTitle = value;
                    break;
                case MermaidChart chart:
                    chart.AccessibleTitle = value;
                    break;
                default:
                    throw Unsupported(content);
            }
        }
    }

    public string? Description
    {
        get => content switch
        {
            Diagram diagram => diagram.Description,
            MermaidChart chart => chart.Description,
            _ => throw Unsupported(content),
        };
        set
        {
            switch (content)
            {
                case Diagram diagram:
                    diagram.Description = value;
                    break;
                case MermaidChart chart:
                    chart.Description = value;
                    break;
                default:
                    throw Unsupported(content);
            }
        }
    }

    public DiagramTheme Theme
    {
        get => content switch
        {
            Diagram diagram => diagram.Theme,
            MermaidChart chart => chart.Theme,
            _ => throw Unsupported(content),
        };
        set
        {
            switch (content)
            {
                case Diagram diagram:
                    diagram.Theme = value;
                    break;
                case MermaidChart chart:
                    chart.Theme = value;
                    break;
                default:
                    throw Unsupported(content);
            }
        }
    }

    /// <summary>Sets the space around a diagram; the charts lay their margins out themselves and ignore it.</summary>
    public void SetPadding(double padding)
    {
        if (content is Diagram diagram)
        {
            diagram.Padding = padding;
        }
    }

    public static ArgumentException Unsupported(ISceneSource content) => new($"The type '{content.GetType()}' is not a Mermaid diagram or chart.", nameof(content));
}
