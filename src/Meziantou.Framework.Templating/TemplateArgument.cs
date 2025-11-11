namespace Meziantou.Framework.Templating;

/// <summary>Represents a named argument that can be passed to a template.</summary>
public class TemplateArgument
{
    /// <summary>Initializes a new instance of the <see cref="TemplateArgument"/> class.</summary>
    /// <param name="name">The name of the argument.</param>
    /// <param name="type">The type of the argument, or <c>null</c> for dynamic type.</param>
    public TemplateArgument(string name, Type? type)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
    }

    /// <summary>Gets the name of the argument.</summary>
    public string Name { get; }

    /// <summary>Gets the type of the argument, or <c>null</c> for dynamic type.</summary>
    public Type? Type { get; }
}
