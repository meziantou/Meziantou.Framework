using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.ClassDiagrams;

/// <summary>Writes a <see cref="ClassDiagram"/> as Mermaid.</summary>
internal static class ClassDiagramWriter
{
    public static void Write(StringBuilder builder, ClassDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.HideEmptyMembersBox)
        {
            configuration.Add("hideEmptyMembersBox: true");
        }

        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        // The namespaces are written as nested blocks, so their dotted names must not nest them again
        if (diagram.EnumerateNamespaces().Any(ns => ns.Id.Contains('.', StringComparison.Ordinal)))
        {
            configuration.Add("hierarchicalNamespaces: false");
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "class", configuration);
        builder.Append("classDiagram\n");
        if (diagram.Direction is not DiagramDirection.TopToBottom)
        {
            builder.Append("    direction ").Append(MermaidCommon.FormatDirection(diagram.Direction)).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        var written = new HashSet<ClassDiagramElement>(ReferenceEqualityComparer.Instance);
        var namespaces = new HashSet<ClassDiagramNamespace>(ReferenceEqualityComparer.Instance);
        foreach (var ns in diagram.Namespaces)
        {
            WriteNamespace(builder, ns, 1, written, namespaces);
        }

        foreach (var item in diagram.Classes)
        {
            WriteClass(builder, item, 1, written);
        }

        foreach (var relation in diagram.Relations)
        {
            foreach (var end in new[] { relation.From, relation.To })
            {
                if (end is ClassDiagramClass item && !written.Contains(item))
                {
                    WriteClass(builder, item, 1, written);
                }
            }
        }

        foreach (var relation in diagram.Relations)
        {
            WriteRelation(builder, relation);
        }

        foreach (var note in diagram.Notes)
        {
            builder.Append("    note ");
            if (note.Class is { } target)
            {
                if (!written.Contains(target))
                {
                    WriteClass(builder.Remove(builder.Length - 9, 9), target, 1, written);
                    builder.Append("    note ");
                }

                builder.Append("for ").Append(FormatName(target.Id)).Append(' ');
            }

            builder.Append(MermaidText.EncodeQuoted(new DiagramText(note.Text.Text))).Append('\n');
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            ValidateClassName(name);
            builder.Append("    classDef ").Append(name).Append(' ').Append(MermaidStyle.Format(style)).Append('\n');
        }

        foreach (var element in written)
        {
            foreach (var className in element.CssClasses)
            {
                ValidateClassName(className);
                builder.Append("    cssClass \"").Append(element.Id).Append("\" ").Append(className).Append('\n');
            }

            if (!MermaidStyle.IsEmpty(element.Style))
            {
                builder.Append("    style ").Append(FormatName(element.Id)).Append(' ').Append(MermaidStyle.Format(element.Style!)).Append('\n');
            }

            if (element.Link is not null || element.Tooltip is not null)
            {
                builder.Append("    click ").Append(FormatName(element.Id)).Append(element.Link is null ? " call callback()" : " href " + Quote(element.Link));
                if (element.Tooltip is not null)
                {
                    builder.Append(' ').Append(Quote(element.Tooltip));
                }

                builder.Append('\n');
            }
        }
    }

    private static void WriteNamespace(StringBuilder builder, ClassDiagramNamespace ns, int indent, HashSet<ClassDiagramElement> written, HashSet<ClassDiagramNamespace> namespaces)
    {
        if (!namespaces.Add(ns) || indent > 64)
            throw new ArgumentException($"The namespace '{ns.Id}' is in the diagram twice.", nameof(ns));

        builder.AppendIndent(indent).Append("namespace ").Append(FormatName(ns.Id));
        if (ns.Label is not null && !string.Equals(ns.Label.Text, ns.Id, StringComparison.Ordinal))
        {
            builder.Append("[\"").Append(MermaidText.EncodePlain(ns.Label.Text)).Append("\"]");
        }

        builder.Append(" {\n");
        foreach (var child in ns.Namespaces)
        {
            WriteNamespace(builder, child, indent + 1, written, namespaces);
        }

        foreach (var item in ns.Classes)
        {
            WriteClass(builder, item, indent + 1, written);
        }

        builder.AppendIndent(indent).Append("}\n");
    }

    private static void WriteClass(StringBuilder builder, ClassDiagramClass item, int indent, HashSet<ClassDiagramElement> written)
    {
        if (!written.Add(item))
            return;

        builder.AppendIndent(indent).Append("class ").Append(FormatName(item.Id));
        if (item.GenericType is { Length: > 0 } generic)
        {
            if (generic.AsSpan().IndexOfAny('\n', '\r') >= 0)
                throw new ArgumentException($"The generic type of '{item.Id}' cannot be written as Mermaid.", nameof(item));

            builder.Append('~').Append(generic).Append('~');
        }

        if (item.Label is not null)
        {
            builder.Append("[\"").Append(MermaidText.EncodePlain(item.Label.Text)).Append("\"]");
        }

        if (item.Annotations.Count is 0 && item.Members.Count is 0)
        {
            builder.Append('\n');
            return;
        }

        builder.Append(" {\n");
        foreach (var annotation in item.Annotations)
        {
            if (annotation.Trim().Length is 0 || annotation.Trim() != annotation)
                throw new ArgumentException($"The annotation '{annotation}' cannot be written as Mermaid.", nameof(item));

            builder.AppendIndent(indent + 1).Append("<<").Append(EncodeAnnotation(annotation)).Append(">>\n");
        }

        foreach (var member in item.Members)
        {
            if (member.Text.Trim().Length is 0)
                throw new ArgumentException($"An empty member of '{item.Id}' cannot be written as Mermaid.", nameof(item));

            builder.AppendIndent(indent + 1).Append(FormatMember(member)).Append('\n');
        }

        builder.AppendIndent(indent).Append("}\n");
    }

    /// <summary>
    /// Writes a member, its visibility first and its classifiers last. The text is read with the entity codes decoded, so
    /// every character that would read differently is an HTML entity (<c>&amp;#123;</c>, which does not start like a
    /// visibility): the braces, the classifiers and the visibility
    /// characters where they would be read as such, the line breaks and the comments.
    /// </summary>
    private static string FormatMember(ClassMember member)
    {
        var text = member.Text.Trim();
        var builder = new StringBuilder();
        builder.Append(member.Visibility switch
        {
            ClassMemberVisibility.Public => "+",
            ClassMemberVisibility.Private => "-",
            ClassMemberVisibility.Protected => "#",
            ClassMemberVisibility.Package => "~",
            _ => string.Empty,
        });

        var close = text.AsSpan().LastIndexOf(')');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var encode = c switch
            {
                '#' or '&' or '}' or '{' or '\n' or '\r' or '\u2028' or '\u2029' or '\u0085' or ';' => true,
                '%' => i + 1 < text.Length && text[i + 1] == '%',
                '$' or '*' => i == text.Length - 1 || i == close + 1,
                '<' => i == 0 && text.Length > 1 && text[1] == '<',
                '+' or '-' or '~' => i == 0,
                _ => false,
            };

            if (encode)
            {
                builder.Append("&#").Append(((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            else
            {
                builder.Append(c);
            }
        }

        if (member.IsAbstract)
        {
            builder.Append('*');
        }

        if (member.IsStatic)
        {
            builder.Append('$');
        }

        return builder.ToString();
    }

    /// <summary>Encodes the characters of an annotation that would end it or the block of the class.</summary>
    private static string EncodeAnnotation(string annotation)
    {
        var builder = new StringBuilder(annotation.Length);
        for (var i = 0; i < annotation.Length; i++)
        {
            var c = annotation[i];
            var encode = c is '#' or '&' or '}' or '{' or '\n' or '\r' or '\u2028' or '\u2029' or '\u0085' || (c == '>' && i + 1 < annotation.Length && annotation[i + 1] == '>') || (c == '>' && i == annotation.Length - 1) || (c == '%' && i + 1 < annotation.Length && annotation[i + 1] == '%');
            if (encode)
            {
                builder.Append("&#").Append(((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static void WriteRelation(StringBuilder builder, ClassRelation relation)
    {
        builder.Append("    ").Append(FormatName(relation.From.Id)).Append(' ');
        if (relation.FromCardinality is { } fromCardinality)
        {
            builder.Append(Quote(fromCardinality)).Append(' ');
        }

        builder.Append(relation.FromEnd switch
        {
            ClassRelationEnd.Arrow => "<",
            ClassRelationEnd.Inheritance => "<|",
            ClassRelationEnd.Composition => "*",
            ClassRelationEnd.Aggregation => "o",
            ClassRelationEnd.Lollipop => "()",
            _ => string.Empty,
        });
        builder.Append(relation.IsDashed ? ".." : "--");
        builder.Append(relation.ToEnd switch
        {
            ClassRelationEnd.Arrow => ">",
            ClassRelationEnd.Inheritance => "|>",
            ClassRelationEnd.Composition => "*",
            ClassRelationEnd.Aggregation => "o",
            ClassRelationEnd.Lollipop => "()",
            _ => string.Empty,
        });
        builder.Append(' ');
        if (relation.ToCardinality is { } toCardinality)
        {
            builder.Append(Quote(toCardinality)).Append(' ');
        }

        builder.Append(FormatName(relation.To.Id));
        if (relation.Label is { IsEmpty: false } label)
        {
            var text = MermaidText.EncodePlain(label.Text).Trim();
            if (text.Length > 0)
            {
                builder.Append(" : ").Append(text);
            }
        }

        builder.Append('\n');
    }

    private static string Quote(string value) => MermaidText.QuoteAttribute(value);

    /// <summary>Writes a name as is when it reads back as itself, and between backticks otherwise.</summary>
    private static string FormatName(string id)
    {
        if (IsSimpleName(id))
            return id;

        if (id.AsSpan().IndexOfAny("`\n\r\",") >= 0)
            throw new ArgumentException($"The name '{id}' cannot be written as Mermaid.", nameof(id));

        return "`" + id + "`";
    }

    private static bool IsSimpleName(string id)
    {
        if (id.Length is 0 || id is "class" or "namespace" or "note" or "direction" or "style" or "classDef" or "cssClass" or "click" or "link" or "callback" || id.StartsWith("accTitle", StringComparison.Ordinal) || id.StartsWith("accDescr", StringComparison.Ordinal))
            return false;

        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (char.IsLetterOrDigit(c) || c == '_')
                continue;

            if (char.IsHighSurrogate(c) && i + 1 < id.Length && char.IsLowSurrogate(id[i + 1]))
            {
                i++;
                continue;
            }

            if (c is '-' or '.' && i > 0 && i + 1 < id.Length && id[i + 1] != c && id[i + 1] is not ('-' or '.' or '|' or '>' or '*' or 'o' or '('))
                continue;

            return false;
        }

        return true;
    }

    private static void ValidateClassName(string name)
    {
        if (name.Length is 0 || name.Any(c => char.IsWhiteSpace(c) || c is ',' or ';'))
            throw new ArgumentException($"The class name '{name}' cannot be written as Mermaid.", nameof(name));
    }
}
