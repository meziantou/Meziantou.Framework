using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.StateDiagrams;

/// <summary>
/// Writes a <see cref="StateDiagram"/> as Mermaid. Every state is declared in its region before the transitions, so the
/// states mentioned by the transitions are already in their region; the start and end states are written <c>[*]</c>, in
/// the region they belong to.
/// </summary>
internal static class StateDiagramWriter
{
    public static void Write(StringBuilder builder, StateDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "state", configuration);
        builder.Append("stateDiagram-v2\n");
        if (diagram.Direction is not DiagramDirection.TopToBottom)
        {
            builder.Append("    direction ").Append(MermaidCommon.FormatDirection(diagram.Direction)).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);

        // The region of each state, to write the transitions of the start and end states in it
        var regionOf = new Dictionary<StateDiagramState, StateDiagramRegion?>(ReferenceEqualityComparer.Instance);
        foreach (var state in diagram.States)
        {
            regionOf.TryAdd(state, null);
        }

        foreach (var state in diagram.EnumerateStates())
        {
            foreach (var region in state.Regions)
            {
                foreach (var child in region.States)
                {
                    regionOf.TryAdd(child, region);
                }
            }
        }

        var transitionsOf = new Dictionary<StateDiagramRegion, List<StateTransition>>(ReferenceEqualityComparer.Instance);
        var rootTransitions = new List<StateTransition>();
        foreach (var transition in diagram.Transitions)
        {
            var special = IsSpecial(transition.From) ? transition.From : IsSpecial(transition.To) ? transition.To : null;
            var region = special is not null && regionOf.TryGetValue(special, out var specialRegion) ? specialRegion : null;
            if (IsSpecial(transition.From) && IsSpecial(transition.To) && regionOf.GetValueOrDefault(transition.From) != regionOf.GetValueOrDefault(transition.To))
                throw new ArgumentException("A transition between start or end states of different regions cannot be written as Mermaid.", nameof(diagram));

            if (region is null)
            {
                rootTransitions.Add(transition);
            }
            else
            {
                if (!transitionsOf.TryGetValue(region, out var list))
                {
                    transitionsOf[region] = list = [];
                }

                list.Add(transition);
            }
        }

        var written = new HashSet<StateDiagramState>(ReferenceEqualityComparer.Instance);
        foreach (var state in diagram.States)
        {
            WriteState(builder, state, 1, written, transitionsOf);
        }

        foreach (var transition in rootTransitions)
        {
            WriteTransition(builder, transition, 1);
        }

        foreach (var note in diagram.Notes)
        {
            var position = note.Position is StateNotePosition.Left ? "left" : "right";
            var lines = MermaidText.EncodePlain(note.Text.Text).Split("<br>").Select(line => line.Trim()).ToList();
            if (lines.Count == 1)
            {
                builder.Append("    note ").Append(position).Append(" of ").Append(FormatId(note.State)).Append(" : ").Append(lines[0]).Append('\n');
            }
            else
            {
                builder.Append("    note ").Append(position).Append(" of ").Append(FormatId(note.State)).Append('\n');
                foreach (var line in lines)
                {
                    if (line is "end note")
                        throw new ArgumentException("A note cannot contain the line 'end note' in Mermaid.", nameof(diagram));

                    builder.Append("        ").Append(line).Append('\n');
                }

                builder.Append("    end note\n");
            }
        }

        foreach (var (name, style) in diagram.ClassDefinitions)
        {
            ValidateClassName(name);
            builder.Append("    classDef ").Append(name).Append(' ').Append(MermaidStyle.Format(style)).Append('\n');
        }

        foreach (var state in written)
        {
            foreach (var className in state.CssClasses)
            {
                ValidateClassName(className);
                builder.Append("    class ").Append(FormatId(state)).Append(' ').Append(className).Append('\n');
            }

            if (!MermaidStyle.IsEmpty(state.Style))
            {
                builder.Append("    style ").Append(FormatId(state)).Append(' ').Append(MermaidStyle.Format(state.Style!)).Append('\n');
            }
        }
    }

    private static void WriteState(StringBuilder builder, StateDiagramState state, int indent, HashSet<StateDiagramState> written, Dictionary<StateDiagramRegion, List<StateTransition>> transitionsOf)
    {
        if (IsSpecial(state))
            return;

        if (!written.Add(state))
            throw new ArgumentException($"The state '{state.Id}' is in the diagram twice.", nameof(state));

        if (indent > 64)
            throw new ArgumentException("The composite states are nested too deeply.", nameof(state));

        var id = FormatId(state);
        var labelLines = state.Label is null ? [] : MermaidText.EncodePlain(state.Label.Text).Split("<br>").Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        builder.AppendIndent(indent).Append("state ").Append(id);
        builder.Append(state.Kind switch
        {
            StateKind.Choice => " <<choice>>",
            StateKind.Fork => " <<fork>>",
            StateKind.Join => " <<join>>",
            _ => string.Empty,
        });

        if (state.IsComposite)
        {
            builder.Append(" {\n");
            if (state.Direction is { } direction)
            {
                builder.AppendIndent(indent + 1).Append("direction ").Append(MermaidCommon.FormatDirection(direction)).Append('\n');
            }

            for (var i = 0; i < state.Regions.Count; i++)
            {
                if (i > 0)
                {
                    builder.AppendIndent(indent + 1).Append("--\n");
                }

                var region = state.Regions[i];
                foreach (var child in region.States)
                {
                    WriteState(builder, child, indent + 1, written, transitionsOf);
                }

                if (transitionsOf.TryGetValue(region, out var transitions))
                {
                    foreach (var transition in transitions)
                    {
                        WriteTransition(builder, transition, indent + 1);
                    }
                }
            }

            builder.AppendIndent(indent).Append("}\n");
        }
        else
        {
            builder.Append('\n');
        }

        foreach (var line in labelLines)
        {
            builder.AppendIndent(indent).Append(id).Append(" : ").Append(line).Append('\n');
        }
    }

    private static void WriteTransition(StringBuilder builder, StateTransition transition, int indent)
    {
        builder.AppendIndent(indent).Append(FormatReference(transition.From)).Append(" --> ").Append(FormatReference(transition.To));
        if (transition.Label is { IsEmpty: false } label)
        {
            var text = MermaidText.EncodePlain(label.Text).Trim();
            if (text.Length > 0)
            {
                builder.Append(" : ").Append(text);
            }
        }

        builder.Append('\n');
    }

    private static bool IsSpecial(StateDiagramState state) => state.Kind is StateKind.Start or StateKind.End;

    private static string FormatReference(StateDiagramState state) => IsSpecial(state) ? "[*]" : FormatId(state);

    private static string FormatId(StateDiagramState state)
    {
        var id = state.Id;
        if (id.Length is 0 || StateDiagramParser.IsKeyword(id))
            throw new ArgumentException($"The state '{id}' cannot be written as Mermaid.", nameof(state));

        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '.' || (c == '-' && i + 1 < id.Length && id[i + 1] is not ('-' or '>')))
                continue;

            if (char.IsHighSurrogate(c) && i + 1 < id.Length && char.IsLowSurrogate(id[i + 1]))
            {
                i++;
                continue;
            }

            throw new ArgumentException($"The state '{id}' cannot be written as Mermaid.", nameof(state));
        }

        return id;
    }

    private static void ValidateClassName(string name)
    {
        if (name.Length is 0 || name.Any(c => char.IsWhiteSpace(c) || c is ',' or ';'))
            throw new ArgumentException($"The class name '{name}' cannot be written as Mermaid.", nameof(name));
    }
}
