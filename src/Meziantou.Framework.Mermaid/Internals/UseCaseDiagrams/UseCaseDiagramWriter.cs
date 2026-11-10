using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.UseCaseDiagrams;

/// <summary>Writes a <see cref="UseCaseDiagram"/> as Mermaid: the declarations, then the relationships, the notes and the styles.</summary>
internal static class UseCaseDiagramWriter
{
    // The words that start a statement, which a declaration without label cannot use
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal) { "actor", "systemBoundary", "direction", "note", "json", "classDef", "class", "style", "accTitle", "accDescr", "end" };

    public static void Write(StringBuilder builder, UseCaseDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.NodeSpacing != 50)
        {
            configuration.Add("nodeSpacing: " + MermaidStyle.Number(diagram.NodeSpacing));
        }

        if (diagram.RankSpacing != 50)
        {
            configuration.Add("rankSpacing: " + MermaidStyle.Number(diagram.RankSpacing));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "usecase", configuration);
        builder.Append("usecase-beta\n");
        if (diagram.Direction is not DiagramDirection.LeftToRight)
        {
            builder.Append("direction ").Append(MermaidCommon.FormatDirection(diagram.Direction)).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 0);

        // Every identifier is unique in the diagram: elements, boundaries and relationships
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var owner = new Dictionary<UseCaseDiagramElement, UseCaseBoundary>(ReferenceEqualityComparer.Instance);
        foreach (var boundary in diagram.Boundaries)
        {
            foreach (var element in boundary.Elements)
            {
                if (element is UseCaseJsonTable)
                    throw new ArgumentException($"The table '{element.Id}' cannot be in a boundary in Mermaid.", nameof(diagram));

                if (owner.TryGetValue(element, out var other) && other != boundary)
                    throw new ArgumentException($"'{element.Id}' is in several boundaries.", nameof(diagram));

                owner[element] = boundary;
            }
        }

        var elements = new List<UseCaseDiagramElement>();
        var seen = new HashSet<UseCaseDiagramElement>(ReferenceEqualityComparer.Instance);
        foreach (var element in diagram.Actors.Cast<UseCaseDiagramElement>().Concat(diagram.UseCases).Concat(diagram.Tables).Concat(diagram.Boundaries.SelectMany(boundary => boundary.Elements)).Concat(diagram.Relationships.SelectMany(relationship => new[] { relationship.From, relationship.To })).Concat(diagram.Notes.Select(note => note.Target)))
        {
            if (seen.Add(element))
            {
                if (!ids.Add(ValidateId(element.Id)))
                    throw new ArgumentException($"The identifier '{element.Id}' is used several times.", nameof(diagram));

                elements.Add(element);
            }
        }

        var boundaries = new List<UseCaseBoundary>();
        foreach (var boundary in diagram.Boundaries)
        {
            if (boundaries.Contains(boundary))
                continue;

            if (!ids.Add(ValidateId(boundary.Id)))
                throw new ArgumentException($"The identifier '{boundary.Id}' is used several times.", nameof(diagram));

            boundaries.Add(boundary);
        }

        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.Id is { } id && !ids.Add(ValidateId(id)))
                throw new ArgumentException($"The identifier '{id}' is used several times.", nameof(diagram));
        }

        var declared = new List<UseCaseDiagramElement>();
        foreach (var element in elements)
        {
            if (element is UseCaseActor && !owner.ContainsKey(element))
            {
                WriteElement(builder, element, indent: 0, declared);
            }
        }

        foreach (var boundary in boundaries)
        {
            builder.Append("systemBoundary ").Append(boundary.Id);
            if (boundary.Title is { } title && !(title.Format is DiagramTextFormat.Plain && string.Equals(title.Text, boundary.Id, StringComparison.Ordinal)))
            {
                builder.Append('[').Append(FormatString(title)).Append(']');
            }

            if (boundary.Kind is UseCaseBoundaryKind.Package)
            {
                builder.Append("@{ type: package }");
            }

            AppendClasses(builder, boundary.CssClasses);
            builder.Append('\n');
            foreach (var element in boundary.Elements)
            {
                if (owner[element] == boundary)
                {
                    WriteElement(builder, element, indent: 1, declared);
                }
            }

            builder.Append("end\n");
        }

        foreach (var element in elements.Where(element => element is UseCase).Concat(elements.Where(element => element is UseCaseJsonTable)))
        {
            if (!owner.ContainsKey(element))
            {
                WriteElement(builder, element, indent: 0, declared);
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            WriteRelationship(builder, relationship);
        }

        foreach (var note in diagram.Notes)
        {
            if (note.Target is not (UseCaseActor or UseCase))
                throw new ArgumentException("A note is attached to an actor or a use case.", nameof(diagram));

            builder.Append("note for ").Append(note.Target.Id).Append(' ').Append(FormatString(note.Text)).Append('\n');
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            ValidateClassName(name);
            builder.Append("classDef ").Append(name).Append(' ').Append(MermaidStyle.Format(style)).Append('\n');
        }

        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.CssClasses.Count > 0 || !MermaidStyle.IsEmpty(relationship.Style))
            {
                var id = relationship.Id ?? throw new ArgumentException("A relationship with classes or a style needs an identifier in Mermaid.", nameof(diagram));
                foreach (var className in relationship.CssClasses)
                {
                    ValidateClassName(className);
                    builder.Append("class ").Append(id).Append(' ').Append(className).Append('\n');
                }
            }
        }

        // In the order of the declarations, which is the order the parser reads the elements in
        foreach (var (id, style) in declared.Select(element => (element.Id, element.Style)).Concat(boundaries.Select(boundary => (boundary.Id, boundary.Style))).Concat(diagram.Relationships.Where(relationship => relationship.Id is not null).Select(relationship => (relationship.Id!, relationship.Style))))
        {
            if (!MermaidStyle.IsEmpty(style))
            {
                builder.Append("style ").Append(id).Append(' ').Append(MermaidStyle.Format(style!)).Append('\n');
            }
        }
    }

    private static void WriteElement(StringBuilder builder, UseCaseDiagramElement element, int indent, List<UseCaseDiagramElement> declared)
    {
        declared.Add(element);
        builder.Append(' ', 2 * indent);
        switch (element)
        {
            case UseCaseActor actor:
                builder.Append("actor ").Append(actor.Id);
                if (actor.Label is { } actorLabel)
                {
                    builder.Append('(').Append(FormatString(actorLabel)).Append(')');
                }

                var metadata = new List<string>();
                if (actor.Kind is not UseCaseActorKind.Normal)
                {
                    metadata.Add("type: " + (actor.Kind is UseCaseActorKind.Hollow ? "hollow" : "awesome"));
                }

                if (actor.IsBusiness)
                {
                    if (actor.Kind is UseCaseActorKind.Awesome)
                        throw new ArgumentException($"The awesome actor '{actor.Id}' cannot be a business actor.", nameof(element));

                    metadata.Add("business: true");
                }

                AppendMetadata(builder, metadata);
                AppendStereotype(builder, actor.Stereotype);
                break;
            case UseCase useCase:
                builder.Append(useCase.Id);
                var isRectangle = useCase.Shape is UseCaseShape.Rectangle;
                if (useCase.Label is { } label)
                {
                    builder.Append(isRectangle ? '[' : '(').Append(FormatString(label)).Append(isRectangle ? ']' : ')');
                }
                else if (isRectangle || Keywords.Contains(useCase.Id))
                {
                    // The brackets make a rectangle, and a keyword cannot start a statement
                    builder.Append(isRectangle ? '[' : '(').Append(FormatString(new DiagramText(useCase.Id))).Append(isRectangle ? ']' : ')');
                }

                if (useCase.IsBusiness)
                {
                    if (isRectangle)
                        throw new ArgumentException($"The rectangular use case '{useCase.Id}' cannot be a business use case.", nameof(element));

                    AppendMetadata(builder, ["business: true"]);
                }

                AppendStereotype(builder, useCase.Stereotype);
                break;
            case UseCaseJsonTable table:
                builder.Append("json ").Append(table.Id).Append('@').Append(table.Json.Trim());
                break;
        }

        AppendClasses(builder, element.CssClasses);
        builder.Append('\n');
    }

    private static void WriteRelationship(StringBuilder builder, UseCaseRelationship relationship)
    {
        builder.Append(relationship.From.Id).Append(' ');
        if (relationship.Id is { } id)
        {
            builder.Append(id).Append('@');
        }

        var extra = new string('-', Math.Clamp(relationship.MinLength, 1, 100) - 1);
        var label = relationship.Label is { IsEmpty: false } text ? FormatString(text) : null;
        switch (relationship.Kind)
        {
            case UseCaseRelationshipKind.Include or UseCaseRelationshipKind.Extend:
                if (relationship.From is not UseCase || relationship.To is not UseCase)
                    throw new ArgumentException("An include or an extend links two use cases.", nameof(relationship));

                builder.Append("..> : ").Append(relationship.Kind is UseCaseRelationshipKind.Include ? "include" : "extend");
                break;
            case UseCaseRelationshipKind.Generalization:
                if (!((relationship.From is UseCaseActor && relationship.To is UseCaseActor) || (relationship.From is UseCase && relationship.To is UseCase)))
                    throw new ArgumentException("A generalization links two actors or two use cases.", nameof(relationship));

                builder.Append("--|>");
                break;
            default:
                var isTable = relationship.From is UseCaseJsonTable || relationship.To is UseCaseJsonTable;
                switch (relationship.FromArrowHead, relationship.ToArrowHead)
                {
                    case (UseCaseArrowHead.None, UseCaseArrowHead.Arrow):
                        builder.Append(label is null ? "--" + extra + ">" : "-- " + label + " --" + extra + ">");
                        break;
                    case (UseCaseArrowHead.Arrow, UseCaseArrowHead.None):
                        builder.Append(label is null ? "<--" + extra : "<--" + extra + " " + label + " --");
                        break;
                    case (UseCaseArrowHead.None, UseCaseArrowHead.None):
                        builder.Append(label is null ? "--" + extra : "-- " + label + " --" + extra);
                        break;
                    case (UseCaseArrowHead.None, UseCaseArrowHead.Circle or UseCaseArrowHead.Cross) when !isTable:
                        var to = relationship.ToArrowHead is UseCaseArrowHead.Circle ? "--o" : "--x";
                        builder.Append(label is null ? to : "-- " + label + " " + to);
                        break;
                    case (UseCaseArrowHead.Circle or UseCaseArrowHead.Cross, UseCaseArrowHead.None) when !isTable:
                        var from = relationship.FromArrowHead is UseCaseArrowHead.Circle ? "o--" : "x--";
                        builder.Append(label is null ? from : from + " " + label + " --");
                        break;
                    default:
                        throw new ArgumentException("The association cannot be written as Mermaid: it has a decoration at both ends, or a circle or a cross at a table.", nameof(relationship));
                }

                break;
        }

        builder.Append(' ').Append(relationship.To.Id).Append('\n');
    }

    /// <summary>Writes a label as a plain string, or as a markdown string.</summary>
    private static string FormatString(DiagramText text) => MermaidText.EncodeQuoted(text);

    private static void AppendMetadata(StringBuilder builder, List<string> properties)
    {
        if (properties.Count > 0)
        {
            builder.Append("@{ ").Append(string.Join(", ", properties)).Append(" }");
        }
    }

    private static void AppendStereotype(StringBuilder builder, string? stereotype)
    {
        if (string.IsNullOrEmpty(stereotype))
            return;

        if (stereotype.Trim().Length != stereotype.Length || stereotype.Contains(">>", StringComparison.Ordinal) || stereotype.AsSpan().IndexOfAny('\n', '\r') >= 0)
            throw new ArgumentException($"The stereotype '{stereotype}' cannot be written as Mermaid.", nameof(stereotype));

        // The stereotype ends at the first >>: a space keeps a final > in the text, and the reading trims it
        builder.Append(" <<").Append(stereotype).Append(stereotype.EndsWith('>', StringComparison.Ordinal) ? " >>" : ">>");
    }

    private static void AppendClasses(StringBuilder builder, IList<string> classes)
    {
        if (classes.Count is 0)
            return;

        foreach (var className in classes)
        {
            ValidateClassName(className);
        }

        builder.Append(":::").Append(string.Join(',', classes));
    }

    private static string ValidateId(string id)
    {
        if (!UseCaseDiagramParser.IsIdentifier(id))
            throw new ArgumentException($"The identifier '{id}' cannot be written as Mermaid: it must have only letters, digits and '_'.", nameof(id));

        return id;
    }

    private static void ValidateClassName(string name)
    {
        if (!UseCaseDiagramParser.IsClassName(name))
            throw new ArgumentException($"The class name '{name}' cannot be written as Mermaid.", nameof(name));
    }
}
