using System.Text;
using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A class diagram: classes with their attributes and methods, linked by relations (inheritance, composition,
/// association...), grouped in namespaces (Mermaid <c>classDiagram</c>).
/// </summary>
public sealed class ClassDiagram : Diagram
{
    /// <summary>Gets or sets the direction of the relations. The default is <see cref="DiagramDirection.TopToBottom"/>.</summary>
    public DiagramDirection Direction { get; set; }

    /// <summary>Gets the classes. The classes of the namespaces that are not in this list are drawn too.</summary>
    public IList<ClassDiagramClass> Classes { get; } = new List<ClassDiagramClass>();

    /// <summary>Gets the interfaces drawn as lollipops.</summary>
    public IList<ClassDiagramInterface> Interfaces { get; } = new List<ClassDiagramInterface>();

    /// <summary>Gets the relations, drawn in order.</summary>
    public IList<ClassRelation> Relations { get; } = new List<ClassRelation>();

    /// <summary>Gets the top-level namespaces.</summary>
    public IList<ClassDiagramNamespace> Namespaces { get; } = new List<ClassDiagramNamespace>();

    /// <summary>Gets the notes.</summary>
    public IList<ClassDiagramNote> Notes { get; } = new List<ClassDiagramNote>();

    /// <summary>Gets the styles of the CSS classes of the elements, by name (Mermaid <c>classDef</c>). The class <c>default</c> applies to every class.</summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Gets or sets a value that indicates whether a class without members has no empty compartments (Mermaid <c>hideEmptyMembersBox</c>).</summary>
    public bool HideEmptyMembersBox { get; set; }

    /// <summary>Adds a class.</summary>
    /// <param name="id">The identifier, which is the name shown when there is no label.</param>
    /// <param name="label">The name shown, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The class.</returns>
    public ClassDiagramClass AddClass(string id, DiagramText? label = null)
    {
        var result = new ClassDiagramClass(id, label);
        Classes.Add(result);
        return result;
    }

    /// <summary>Adds a relation.</summary>
    /// <param name="from">The first element.</param>
    /// <param name="to">The second element.</param>
    /// <param name="fromEnd">The decoration at <paramref name="from"/>.</param>
    /// <param name="toEnd">The decoration at <paramref name="to"/>.</param>
    /// <param name="label">The text of the relation, or <see langword="null"/> for none.</param>
    /// <returns>The relation.</returns>
    public ClassRelation AddRelation(ClassDiagramElement from, ClassDiagramElement to, ClassRelationEnd fromEnd = ClassRelationEnd.None, ClassRelationEnd toEnd = ClassRelationEnd.None, DiagramText? label = null)
    {
        var relation = new ClassRelation(from, to) { FromEnd = fromEnd, ToEnd = toEnd, Label = label };
        Relations.Add(relation);
        return relation;
    }

    /// <summary>Adds a top-level namespace.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="label">The title, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The namespace.</returns>
    public ClassDiagramNamespace AddNamespace(string id, DiagramText? label = null)
    {
        var result = new ClassDiagramNamespace(id, label);
        Namespaces.Add(result);
        return result;
    }

    /// <summary>Adds a note.</summary>
    /// <param name="text">The text.</param>
    /// <param name="class">The class the note is attached to, or <see langword="null"/> for a note alone.</param>
    /// <returns>The note.</returns>
    public ClassDiagramNote AddNote(DiagramText text, ClassDiagramClass? @class = null)
    {
        var note = new ClassDiagramNote(text, @class);
        Notes.Add(note);
        return note;
    }

    /// <summary>Finds a class or an interface by identifier, in the diagram and its namespaces.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The element, or <see langword="null"/> when there is none.</returns>
    public ClassDiagramElement? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var item in Classes)
        {
            if (string.Equals(item.Id, id, StringComparison.Ordinal))
                return item;
        }

        foreach (var item in Interfaces)
        {
            if (string.Equals(item.Id, id, StringComparison.Ordinal))
                return item;
        }

        foreach (var ns in EnumerateNamespaces())
        {
            foreach (var item in ns.Classes)
            {
                if (string.Equals(item.Id, id, StringComparison.Ordinal))
                    return item;
            }
        }

        return null;
    }

    /// <summary>Enumerates the namespaces and their nested namespaces, depth first.</summary>
    /// <returns>The namespaces.</returns>
    public IEnumerable<ClassDiagramNamespace> EnumerateNamespaces()
    {
        var visited = new HashSet<ClassDiagramNamespace>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<ClassDiagramNamespace>(Namespaces.Reverse());
        while (stack.Count > 0)
        {
            var ns = stack.Pop();
            if (!visited.Add(ns))
                continue;

            yield return ns;
            for (var i = ns.Namespaces.Count - 1; i >= 0; i--)
            {
                stack.Push(ns.Namespaces[i]);
            }
        }
    }

    /// <summary>Replaces the generic types of Mermaid, <c>List~int~</c>, by angle brackets: <c>List&lt;int&gt;</c>.</summary>
    internal static string FormatGenerics(string text)
    {
        if (!text.Contains('~', StringComparison.Ordinal))
            return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '~')
            {
                builder.Append(text[i]);
                continue;
            }

            // An opening tilde follows a name and precedes one; the others close
            var previous = i > 0 ? text[i - 1] : ' ';
            var next = i + 1 < text.Length ? text[i + 1] : ' ';
            var opens = (char.IsLetterOrDigit(previous) || previous is '_' or ']' or '>') && (char.IsLetterOrDigit(next) || next is '_' or '[');
            builder.Append(opens ? '<' : '>');
        }

        return builder.ToString();
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => ClassDiagramRenderer.Render(this, context, group);
}
