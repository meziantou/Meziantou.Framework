namespace Meziantou.Framework.Templating;

/// <summary>Represents a directive block in a template.</summary>
public class DirectiveBlock : TemplateBlock
{
    /// <summary>Initializes a new instance of the <see cref="DirectiveBlock"/> class.</summary>
    /// <param name="template">The template that contains this block.</param>
    /// <param name="text">The original directive text.</param>
    /// <param name="index">The index of this block in the template.</param>
    /// <param name="name">The directive name.</param>
    /// <param name="value">The directive value.</param>
    public DirectiveBlock(Template template, string text, int index, string name, string value)
        : base(template, text, index)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the directive name.</summary>
    public string Name { get; }

    /// <summary>Gets the directive value.</summary>
    public string Value { get; }

    /// <summary>Builds the C# code for this directive block.</summary>
    /// <returns>An empty string as directives are not emitted as executable code by default.</returns>
    public override string BuildCode()
    {
        return string.Empty;
    }

    /// <summary>Applies the directive to the template.</summary>
    public virtual void ApplyDirective()
    {
        if (string.Equals(Name, "using", StringComparison.OrdinalIgnoreCase))
        {
            Template.AddUsing(Value);
        }
        else if (string.Equals(Name, "inherits", StringComparison.OrdinalIgnoreCase))
        {
            Template.BaseClassFullTypeName = Value;
        }
        else if (string.Equals(Name, "implements", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var @interface in Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                Template.AddImplementedInterface(@interface);
            }
        }
        else if (string.Equals(Name, "reference", StringComparison.OrdinalIgnoreCase))
        {
            Template.AddReference(Value);
        }
    }
}
