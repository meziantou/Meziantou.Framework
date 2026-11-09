namespace Meziantou.Framework.Diagrams;

/// <summary>An attribute or a method of a class.</summary>
public sealed class ClassMember
{
    /// <summary>Initializes a new instance of the <see cref="ClassMember"/> class.</summary>
    /// <param name="text">The member, such as <c>String name</c> or <c>getName() String</c>. The generic types are written <c>List~int~</c>, as in Mermaid, and shown <c>List&lt;int&gt;</c>.</param>
    public ClassMember(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>Gets the member, without its visibility.</summary>
    public string Text { get; }

    /// <summary>Gets or sets the visibility.</summary>
    public ClassMemberVisibility Visibility { get; set; }

    /// <summary>Gets or sets a value that indicates whether the member is static: it is underlined (Mermaid <c>$</c>).</summary>
    public bool IsStatic { get; set; }

    /// <summary>Gets or sets a value that indicates whether the member is abstract: it is in italic (Mermaid <c>*</c>).</summary>
    public bool IsAbstract { get; set; }

    /// <summary>Gets a value that indicates whether the member is a method: its text has parentheses.</summary>
    public bool IsMethod => Text.Contains('(', StringComparison.Ordinal);

    /// <summary>Returns the member as it is shown: its visibility, its text with the generic types between angle brackets, and the return type of a method after a colon.</summary>
    /// <returns>The text shown.</returns>
    public string GetDisplayText()
    {
        var visibility = Visibility switch
        {
            ClassMemberVisibility.Public => "+",
            ClassMemberVisibility.Private => "-",
            ClassMemberVisibility.Protected => "#",
            ClassMemberVisibility.Package => "~",
            _ => string.Empty,
        };

        var text = ClassDiagram.FormatGenerics(Text.Trim());
        var close = text.AsSpan().LastIndexOf(')');
        if (IsMethod && close >= 0 && close < text.Length - 1)
        {
            var returnType = text[(close + 1)..].Trim();
            if (returnType.Length > 0 && returnType[0] != ':')
            {
                text = text[..(close + 1)] + " : " + returnType;
            }
        }

        return visibility + text;
    }

    /// <summary>Returns the text shown.</summary>
    /// <returns>The text shown.</returns>
    public override string ToString() => GetDisplayText();
}
