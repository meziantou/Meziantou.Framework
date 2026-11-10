namespace Meziantou.Framework.Diagrams;

/// <summary>A cause of an Ishikawa diagram: a bone with a text, and the causes that lead to it as smaller bones.</summary>
public sealed class IshikawaCause
{
    private DiagramText _label;

    /// <summary>Initializes a new instance of the <see cref="IshikawaCause"/> class.</summary>
    /// <param name="label">The text of the cause.</param>
    public IshikawaCause(DiagramText label)
    {
        ArgumentNullException.ThrowIfNull(label);
        _label = label;
    }

    /// <summary>Gets or sets the text of the cause. A line feed starts a new line.</summary>
    public DiagramText Label
    {
        get => _label;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _label = value;
        }
    }

    /// <summary>Gets the causes of this cause.</summary>
    public IList<IshikawaCause> Causes { get; } = new List<IshikawaCause>();

    /// <summary>Adds a cause of this cause.</summary>
    /// <param name="label">The text of the cause.</param>
    /// <returns>The cause.</returns>
    public IshikawaCause AddCause(DiagramText label)
    {
        var cause = new IshikawaCause(label);
        Causes.Add(cause);
        return cause;
    }

    /// <summary>Returns the text of the cause.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Label.Text;
}
