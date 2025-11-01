namespace Meziantou.Framework.Templating;

/// <summary>
/// Represents an argument that can be passed to a template.
/// </summary>
public class TemplateArgument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateArgument"/> class.
    /// </summary>
    /// <param name="name">The name of the argument.</param>
    /// <param name="type">The type of the argument.</param>
    public TemplateArgument(string name, Type? type)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
    }

    /// <summary>
    /// Gets the name of the argument.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the type of the argument.
    /// </summary>
    public Type? Type { get; }
}
