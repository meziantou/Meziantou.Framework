namespace Meziantou.Framework.Diagrams;

/// <summary>A class of a class diagram: a box with its name, its annotations, its attributes and its methods.</summary>
public sealed class ClassDiagramClass : ClassDiagramElement
{
    /// <summary>Initializes a new instance of the <see cref="ClassDiagramClass"/> class.</summary>
    /// <param name="id">The identifier, which is the name shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    public ClassDiagramClass(string id, DiagramText? label = null)
        : base(id)
    {
        Label = label;
    }

    /// <summary>Gets or sets the generic type of the class, shown between angle brackets after its name, such as <c>T</c> (Mermaid <c>Square~T~</c>).</summary>
    public string? GenericType { get; set; }

    /// <summary>Gets the annotations of the class, shown above its name between guillemets, such as <c>interface</c> (Mermaid <c>&lt;&lt;interface&gt;&gt;</c>).</summary>
    public IList<string> Annotations { get; } = new List<string>();

    /// <summary>Gets the attributes and the methods, in order. The attributes are drawn above the methods.</summary>
    public IList<ClassMember> Members { get; } = new List<ClassMember>();

    /// <summary>Adds an attribute or a method.</summary>
    /// <param name="text">The member, such as <c>String name</c> or <c>getName() String</c>: a text with parentheses is a method.</param>
    /// <param name="visibility">The visibility.</param>
    /// <returns>The member.</returns>
    public ClassMember AddMember(string text, ClassMemberVisibility visibility = ClassMemberVisibility.None)
    {
        var member = new ClassMember(text) { Visibility = visibility };
        Members.Add(member);
        return member;
    }
}
