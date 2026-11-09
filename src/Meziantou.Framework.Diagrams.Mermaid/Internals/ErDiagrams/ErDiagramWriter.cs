using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.ErDiagrams;

/// <summary>
/// Writes an <see cref="ErDiagram"/> as Mermaid: the subgraphs with the declarations of their entities, the other
/// entities, then the relationships, so the names of the subgraphs are known when the relationships use them.
/// </summary>
internal static class ErDiagramWriter
{
    public static void Write(StringBuilder builder, ErDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.NodeSpacing != 140)
        {
            configuration.Add("nodeSpacing: " + MermaidStyle.Number(diagram.NodeSpacing));
        }

        if (diagram.RankSpacing != 80)
        {
            configuration.Add("rankSpacing: " + MermaidStyle.Number(diagram.RankSpacing));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "er", configuration);
        builder.Append("erDiagram\n");
        if (diagram.Direction is not DiagramDirection.TopToBottom)
        {
            builder.Append("    direction ").Append(MermaidCommon.FormatDirection(diagram.Direction)).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);

        // An entity of several subgraphs belongs to the last one the drawing visits, as in the renderer
        var owner = new Dictionary<ErEntity, ErSubgraph>(ReferenceEqualityComparer.Instance);
        var subgraphIds = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<ErSubgraph>(ReferenceEqualityComparer.Instance);
        foreach (var subgraph in diagram.Subgraphs)
        {
            CollectOwners(subgraph, owner, subgraphIds, visited, 0);
        }

        var written = new HashSet<ErEntity>(ReferenceEqualityComparer.Instance);
        var writtenSubgraphs = new List<ErSubgraph>();
        foreach (var subgraph in diagram.Subgraphs)
        {
            WriteSubgraph(builder, subgraph, owner, subgraphIds, written, writtenSubgraphs, 1);
        }

        foreach (var entity in diagram.Entities)
        {
            if (!owner.ContainsKey(entity) && !written.Contains(entity))
            {
                WriteEntity(builder, entity, subgraphIds, written, 1);
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.From is ErEntity from && !written.Contains(from))
            {
                WriteEntity(builder, from, subgraphIds, written, 1);
            }

            if (relationship.To is ErEntity to && !written.Contains(to))
            {
                WriteEntity(builder, to, subgraphIds, written, 1);
            }
        }

        foreach (var relationship in diagram.Relationships)
        {
            if (relationship.ToCardinality is ErCardinality.Parent)
                throw new ArgumentException("The parent of a master-detail relationship can be written only on the left side in Mermaid.", nameof(diagram));

            builder.Append("    ").Append(FormatName(relationship.From.Id)).Append(' ')
                .Append(relationship.FromCardinality switch
                {
                    ErCardinality.ZeroOrOne => "|o",
                    ErCardinality.OneOrMore => "}|",
                    ErCardinality.ZeroOrMore => "}o",
                    ErCardinality.Parent => "u",
                    _ => "||",
                })
                .Append(relationship.IsIdentifying ? "--" : "..")
                .Append(relationship.ToCardinality switch
                {
                    ErCardinality.ZeroOrOne => "o|",
                    ErCardinality.OneOrMore => "|{",
                    ErCardinality.ZeroOrMore => "o{",
                    _ => "||",
                })
                .Append(' ').Append(FormatName(relationship.To.Id)).Append(" : ").Append(FormatLabel(relationship.Label)).Append('\n');
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            ValidateClassName(name);
            builder.Append("    classDef ").Append(name).Append(' ').Append(MermaidStyle.Format(style)).Append('\n');
        }

        foreach (var subgraph in writtenSubgraphs)
        {
            foreach (var className in subgraph.CssClasses)
            {
                ValidateClassName(className);
                builder.Append("    class ").Append(FormatReference(subgraph.Id)).Append(' ').Append(className).Append('\n');
            }
        }

        foreach (var element in writtenSubgraphs.Cast<ErElement>().Concat(written))
        {
            if (!MermaidStyle.IsEmpty(element.Style))
            {
                builder.Append("    style ").Append(FormatReference(element.Id)).Append(' ').Append(MermaidStyle.Format(element.Style!)).Append('\n');
            }
        }
    }

    private static void CollectOwners(ErSubgraph subgraph, Dictionary<ErEntity, ErSubgraph> owner, HashSet<string> subgraphIds, HashSet<ErSubgraph> visited, int depth)
    {
        if (depth > 64)
            throw new ArgumentException("The subgraphs are nested too deeply.", nameof(subgraph));

        if (!visited.Add(subgraph))
            return;

        if (!subgraphIds.Add(subgraph.Id))
            throw new ArgumentException($"The subgraph '{subgraph.Id}' is in the diagram twice.", nameof(subgraph));

        foreach (var entity in subgraph.Entities)
        {
            owner[entity] = subgraph;
        }

        foreach (var child in subgraph.Subgraphs)
        {
            CollectOwners(child, owner, subgraphIds, visited, depth + 1);
        }
    }

    private static void WriteSubgraph(StringBuilder builder, ErSubgraph subgraph, Dictionary<ErEntity, ErSubgraph> owner, HashSet<string> subgraphIds, HashSet<ErEntity> written, List<ErSubgraph> writtenSubgraphs, int indent)
    {
        if (writtenSubgraphs.Contains(subgraph))
            return;

        writtenSubgraphs.Add(subgraph);
        builder.AppendIndent(indent).Append("subgraph ").Append(FormatName(subgraph.Id));
        if (subgraph.Label is { } label && !string.Equals(label.Text, subgraph.Id, StringComparison.Ordinal))
        {
            if (!ErDiagramParser.IsValidQuotedName(label.Text))
                throw new ArgumentException($"The title of the subgraph '{subgraph.Id}' cannot be written as Mermaid.", nameof(subgraph));

            builder.Append(" [\"").Append(label.Text).Append("\"]");
        }

        builder.Append('\n');
        if (subgraph.Direction is { } direction)
        {
            builder.AppendIndent(indent + 1).Append("direction ").Append(MermaidCommon.FormatDirection(direction)).Append('\n');
        }

        foreach (var entity in subgraph.Entities)
        {
            if (owner.TryGetValue(entity, out var entityOwner) && entityOwner == subgraph && !written.Contains(entity))
            {
                WriteEntity(builder, entity, subgraphIds, written, indent + 1);
            }
        }

        foreach (var child in subgraph.Subgraphs)
        {
            WriteSubgraph(builder, child, owner, subgraphIds, written, writtenSubgraphs, indent + 1);
        }

        builder.AppendIndent(indent).Append("end\n");
    }

    private static void WriteEntity(StringBuilder builder, ErEntity entity, HashSet<string> subgraphIds, HashSet<ErEntity> written, int indent)
    {
        if (subgraphIds.Contains(entity.Id))
            throw new ArgumentException($"The entity '{entity.Id}' has the identifier of a subgraph.", nameof(entity));

        if (!written.Add(entity))
            return;

        builder.AppendIndent(indent).Append(FormatName(entity.Id));
        if (entity.Label is { } label && !string.Equals(label.Text, entity.Id, StringComparison.Ordinal))
        {
            if (!ErDiagramParser.IsValidQuotedName(label.Text))
                throw new ArgumentException($"The label of the entity '{entity.Id}' cannot be written as Mermaid.", nameof(entity));

            builder.Append("[\"").Append(label.Text).Append("\"]");
        }

        if (entity.CssClasses.Count > 0)
        {
            foreach (var className in entity.CssClasses)
            {
                ValidateClassName(className);
            }

            builder.Append(":::").Append(string.Join(',', entity.CssClasses));
        }

        if (entity.Attributes.Count > 0)
        {
            builder.Append(" {\n");
            foreach (var attribute in entity.Attributes)
            {
                builder.AppendIndent(indent + 1).Append(FormatWord(attribute.Type)).Append(' ').Append(FormatWord(attribute.Name));
                if (attribute.Keys is not ErAttributeKeys.None)
                {
                    builder.Append(' ').Append(FormatKeys(attribute.Keys));
                }

                if (!string.IsNullOrEmpty(attribute.Comment))
                {
                    if (attribute.Comment.AsSpan().IndexOfAny('"', '\n', '\r') >= 0)
                        throw new ArgumentException($"The comment of the attribute '{attribute.Name}' cannot contain a quote or a line break in Mermaid.", nameof(entity));

                    builder.Append(" \"").Append(attribute.Comment).Append('"');
                }

                builder.Append('\n');
            }

            builder.AppendIndent(indent).Append('}');
        }

        builder.Append('\n');
    }

    /// <summary>Writes the name of an entity or a subgraph, quoted when Mermaid would read it otherwise.</summary>
    private static string FormatName(string name)
    {
        if (IsSimpleName(name) && !IsReserved(name) && !(name.Length > 1 && name[0] is 'u' or 'U' && name[1] is '-'))
            return name;

        if (!ErDiagramParser.IsValidQuotedName(name))
            throw new ArgumentException($"The name '{name}' cannot be written as Mermaid.", nameof(name));

        return "\"" + name + "\"";
    }

    /// <summary>Writes the name of an element in a <c>class</c> or <c>style</c> statement, which cannot be quoted.</summary>
    private static string FormatReference(string name)
    {
        if (name.Length is 0 || !name.All(ErDiagramParser.IsNameCharacter))
            throw new ArgumentException($"The element '{name}' cannot be styled in Mermaid: its name has other characters than letters, digits, '_', '-', '*' and '.'.", nameof(name));

        return name;
    }

    private static string FormatLabel(DiagramText? label)
    {
        if (label is null || label.IsEmpty)
            return "\"\"";

        if (IsSimpleLabel(label.Text) && !IsReserved(label.Text))
            return label.Text;

        return MermaidText.EncodeQuoted(new DiagramText(label.Text));
    }

    /// <summary>Writes a word of an attribute, between backticks when Mermaid would not read it as one word.</summary>
    private static string FormatWord(string word)
    {
        if (IsAttributeWord(word) && ErDiagramParser.ParseKey(word) is ErAttributeKeys.None)
            return word;

        if (word.AsSpan().IndexOfAny('`', '\n', '\r') >= 0)
            throw new ArgumentException($"The attribute word '{word}' cannot contain a backtick or a line break in Mermaid.", nameof(word));

        return "`" + word + "`";
    }

    private static string FormatKeys(ErAttributeKeys keys)
    {
        var parts = new List<string>(3);
        if (keys.HasFlag(ErAttributeKeys.PrimaryKey))
        {
            parts.Add("PK");
        }

        if (keys.HasFlag(ErAttributeKeys.ForeignKey))
        {
            parts.Add("FK");
        }

        if (keys.HasFlag(ErAttributeKeys.UniqueKey))
        {
            parts.Add("UK");
        }

        return string.Join(", ", parts);
    }

    private static bool IsReserved(string name) => name.ToUpperInvariant() is "ERDIAGRAM" or "STYLE" or "CLASSDEF" or "CLASS" or "SUBGRAPH" or "END" or "ONE" or "MANY" or "TO" or "DIRECTION" or "ACCTITLE" or "ACCDESCR";

    private static void ValidateClassName(string name)
    {
        if (!ErDiagramParser.IsValidClassName(name))
            throw new ArgumentException($"The class name '{name}' cannot be written as Mermaid.", nameof(name));
    }

    /// <summary>Indicates whether a name is words of letters, digits and <c>_</c>, joined by <c>-</c>, that does not start with a digit.</summary>
    private static bool IsSimpleName(string name)
    {
        if (name.Length is 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || name[^1] == '-')
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
                continue;

            if (c == '-' && name[i - 1] != '-')
                continue;

            return false;
        }

        return true;
    }

    private static bool IsSimpleLabel(string text) => text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>Indicates whether Mermaid reads a word of an attribute as is: <c>[*A-Za-z_\u00C0-\uFFFF][A-Za-z0-9\-_\[\]().,\u00C0-\uFFFF*]*</c>.</summary>
    private static bool IsAttributeWord(string word)
    {
        if (word.Length is 0 || !(char.IsAsciiLetter(word[0]) || word[0] is '*' or '_' or >= '\u00C0'))
            return false;

        foreach (var c in word)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '[' or ']' or '(' or ')' or '.' or ',' or '*' or >= '\u00C0'))
                return false;
        }

        return true;
    }
}
