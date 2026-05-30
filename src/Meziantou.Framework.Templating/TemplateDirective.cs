namespace Meziantou.Framework.Templating;

/// <summary>Represents a parsed directive in a template.</summary>
public class TemplateDirective
{
    /// <summary>Initializes a new instance of the <see cref="TemplateDirective"/> class.</summary>
    /// <param name="name">The directive name.</param>
    /// <param name="value">The directive value.</param>
    public TemplateDirective(string name, string value)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the directive name.</summary>
    public string Name { get; }

    /// <summary>Gets the directive value.</summary>
    public string Value { get; }
}
