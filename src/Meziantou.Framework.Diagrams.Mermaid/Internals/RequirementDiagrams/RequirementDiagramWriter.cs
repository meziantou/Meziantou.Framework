using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.RequirementDiagrams;

/// <summary>Writes a <see cref="RequirementDiagram"/> as Mermaid.</summary>
internal static class RequirementDiagramWriter
{
    public static void Write(StringBuilder builder, RequirementDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "requirement", configuration);
        builder.Append("requirementDiagram\n");
        if (diagram.Direction is not DiagramDirection.TopToBottom)
        {
            builder.Append("    direction ").Append(MermaidCommon.FormatDirection(diagram.Direction)).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var written = new List<RequirementDiagramNode>();
        foreach (var requirement in diagram.Requirements)
        {
            if (!Add(requirement))
                continue;

            var keyword = RequirementDiagramParser.RequirementKinds.First(item => item.Kind == requirement.Kind).Keyword;
            WriteHeader(builder, keyword, requirement);
            if (requirement.RequirementId is { Length: > 0 } id)
            {
                builder.Append("        id: ").Append(FormatValue(new DiagramText(id))).Append('\n');
            }

            if (requirement.Text is { IsEmpty: false } text)
            {
                builder.Append("        text: ").Append(FormatValue(text)).Append('\n');
            }

            if (requirement.Risk is { } risk)
            {
                builder.Append("        risk: ").Append(risk.ToString()).Append('\n');
            }

            if (requirement.VerificationMethod is { } method)
            {
                builder.Append("        verifymethod: ").Append(method.ToString()).Append('\n');
            }

            builder.Append("    }\n");
        }

        foreach (var element in diagram.Elements)
        {
            if (!Add(element))
                continue;

            WriteHeader(builder, "element", element);
            if (element.Type is { IsEmpty: false } type)
            {
                builder.Append("        type: ").Append(FormatValue(type)).Append('\n');
            }

            if (element.DocumentReference is { IsEmpty: false } reference)
            {
                builder.Append("        docref: ").Append(FormatValue(reference)).Append('\n');
            }

            builder.Append("    }\n");
        }

        foreach (var relationship in diagram.Relationships)
        {
            if (!written.Contains(relationship.Source) || !written.Contains(relationship.Target))
                throw new ArgumentException("A relationship links a node that is not in the diagram.", nameof(diagram));

            builder.Append("    ").Append(FormatName(relationship.Source.Name)).Append(" - ")
                .Append(FormatKind(relationship.Kind))
                .Append(" -> ").Append(FormatName(relationship.Target.Name)).Append('\n');
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            ValidateClassName(name);
            builder.Append("    classDef ").Append(name).Append(' ').Append(MermaidStyle.Format(style)).Append('\n');
        }

        foreach (var node in written)
        {
            if (!MermaidStyle.IsEmpty(node.Style))
            {
                builder.Append("    style ").Append(FormatName(node.Name)).Append(' ').Append(MermaidStyle.Format(node.Style!)).Append('\n');
            }
        }

        bool Add(RequirementDiagramNode node)
        {
            if (written.Contains(node))
                return false;

            if (!names.Add(node.Name))
                throw new ArgumentException($"The name '{node.Name}' is used by several nodes.", nameof(diagram));

            written.Add(node);
            return true;
        }
    }

    private static void WriteHeader(StringBuilder builder, string keyword, RequirementDiagramNode node)
    {
        builder.Append("    ").Append(keyword).Append(' ').Append(FormatName(node.Name));
        if (node.CssClasses.Count > 0)
        {
            foreach (var className in node.CssClasses)
            {
                ValidateClassName(className);
            }

            builder.Append(":::").Append(string.Join(',', node.CssClasses));
        }

        builder.Append(" {\n");
    }

    private static string FormatName(string name)
    {
        if (name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !RequirementDiagramParser.Keywords.Contains(name))
            return name;

        if (!RequirementDiagramParser.IsValidQuotedName(name))
            throw new ArgumentException($"The name '{name}' cannot be written as Mermaid.", nameof(name));

        return "\"" + name + "\"";
    }

    /// <summary>Writes a value: as is when Mermaid reads it back the same, quoted otherwise, which makes it markdown.</summary>
    private static string FormatValue(DiagramText value)
    {
        var text = value.Text;
        if (value.Format is DiagramTextFormat.Plain && IsUnquotedValue(text))
            return text;

        if (text.AsSpan().IndexOfAny('"', '\n', '\r') >= 0)
            throw new ArgumentException($"The value '{text}' cannot be written as Mermaid: it contains a quote or a line break.", nameof(value));

        return "\"" + text + "\"";
    }

    /// <summary>Indicates whether the parser reads a value written without quotes back the same.</summary>
    private static bool IsUnquotedValue(string text)
    {
        if (text.Length is 0 || !(char.IsLetterOrDigit(text[0]) || text[0] == '_') || text.Trim().Length != text.Length || text.Contains("%%", StringComparison.Ordinal))
            return false;

        return text.AsSpan().IndexOfAny(":,\n\r{<>-=#") < 0 && !RequirementDiagramParser.Keywords.Contains(RequirementDiagramParser.FirstWord(text));
    }

    private static string FormatKind(RequirementRelationshipKind kind) => kind switch
    {
        RequirementRelationshipKind.Copies => "copies",
        RequirementRelationshipKind.Derives => "derives",
        RequirementRelationshipKind.Satisfies => "satisfies",
        RequirementRelationshipKind.Verifies => "verifies",
        RequirementRelationshipKind.Refines => "refines",
        RequirementRelationshipKind.Traces => "traces",
        _ => "contains",
    };

    private static void ValidateClassName(string name)
    {
        if (!RequirementDiagramParser.IsValidClassName(name))
            throw new ArgumentException($"The class name '{name}' cannot be written as Mermaid.", nameof(name));
    }
}
