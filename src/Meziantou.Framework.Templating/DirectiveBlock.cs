namespace Meziantou.Framework.Templating;

/// <summary>Represents a directive block in a template.</summary>
public class DirectiveBlock : TemplateBlock
{
    /// <summary>Initializes a new instance of the <see cref="DirectiveBlock"/> class.</summary>
    /// <param name="template">The template that contains this block.</param>
    /// <param name="text">The directive content without the directive marker.</param>
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
            Template.Usings.Add(Value);
        }
        else if (string.Equals(Name, "inherits", StringComparison.OrdinalIgnoreCase))
        {
            Template.BaseClassFullTypeName = Value;
        }
        else if (string.Equals(Name, "implements", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var @interface in Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                Template.ImplementedInterfaces.Add(@interface);
            }
        }
        else if (string.Equals(Name, "reference", StringComparison.OrdinalIgnoreCase))
        {
            Template.AssemblyReferences.Add(ParseAssemblyReference(Value));
        }
        else if (string.Equals(Name, "include", StringComparison.OrdinalIgnoreCase))
        {
            Template.IncludedSourceFiles.Add(Value);
        }
    }

    private static AssemblyReference ParseAssemblyReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();
        const string AliasPrefix = "alias=";
        if (trimmedValue.StartsWith(AliasPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var aliasSeparatorIndex = trimmedValue.IndexOfAny([' ', '\t']);
            if (aliasSeparatorIndex <= AliasPrefix.Length)
                throw new ArgumentException("The reference directive alias must contain a value.", nameof(value));

            var alias = trimmedValue[AliasPrefix.Length..aliasSeparatorIndex];
            var path = trimmedValue[(aliasSeparatorIndex + 1)..].TrimStart();
            return new AssemblyReference(path, alias);
        }

        return new AssemblyReference(trimmedValue);
    }
}
