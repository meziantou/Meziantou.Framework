using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.SequenceDiagrams;

/// <summary>
/// Writes a <see cref="SequenceDiagram"/> as Mermaid: the participants, in their boxes, then the steps. A participant
/// that a message creates is declared just before the message.
/// </summary>
internal static class SequenceDiagramWriter
{
    public static void Write(StringBuilder builder, SequenceDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (!diagram.MirrorParticipants)
        {
            configuration.Add("mirrorActors: false");
        }

        // The title is a statement when it can be one, as it reads: the front matter cannot hold every character
        var titleStatement = diagram.Title is { } title && IsTitleStatement(title);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "sequence", configuration, includeTitle: !titleStatement);
        builder.Append("sequenceDiagram\n");
        if (titleStatement)
        {
            builder.Append("    title ").Append(diagram.Title).Append('\n');
        }

        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);

        // The participants a message creates are declared at the message
        var created = new HashSet<SequenceParticipant>(ReferenceEqualityComparer.Instance);
        foreach (var step in diagram.EnumerateSteps())
        {
            if (step is SequenceMessage { CreatesReceiver: true } message && !created.Add(message.To))
                throw new ArgumentException($"The participant '{message.To.Id}' is created twice.", nameof(diagram));
        }

        var boxOf = new Dictionary<SequenceParticipant, SequenceBox>(ReferenceEqualityComparer.Instance);
        foreach (var box in diagram.Boxes)
        {
            foreach (var participant in box.Participants)
            {
                if (created.Contains(participant))
                    throw new ArgumentException($"The created participant '{participant.Id}' cannot be in a box in Mermaid.", nameof(diagram));

                if (!boxOf.TryAdd(participant, box) && boxOf[participant] != box)
                    throw new ArgumentException($"The participant '{participant.Id}' is in several boxes.", nameof(diagram));
            }
        }

        var ids = new Dictionary<string, SequenceParticipant>(StringComparer.Ordinal);
        var declared = new HashSet<SequenceParticipant>(ReferenceEqualityComparer.Instance);
        var writtenBoxes = new HashSet<SequenceBox>(ReferenceEqualityComparer.Instance);
        foreach (var participant in diagram.Participants.Concat(diagram.Boxes.SelectMany(box => box.Participants)))
        {
            if (created.Contains(participant) || declared.Contains(participant))
                continue;

            if (boxOf.TryGetValue(participant, out var box))
            {
                if (writtenBoxes.Add(box))
                {
                    builder.Append("    box").Append(FormatBox(box)).Append('\n');
                    foreach (var member in box.Participants)
                    {
                        if (declared.Add(member))
                        {
                            Register(member, ids);
                            WriteDeclaration(builder, member, 2, isCreated: false);
                        }
                    }

                    builder.Append("    end\n");
                }

                continue;
            }

            declared.Add(participant);
            Register(participant, ids);
            WriteDeclaration(builder, participant, 1, isCreated: false);
        }

        // Empty boxes are written too
        foreach (var box in diagram.Boxes)
        {
            if (writtenBoxes.Add(box))
            {
                builder.Append("    box").Append(FormatBox(box)).Append("\n    end\n");
            }
        }

        var state = new WriterState(ids, declared);
        WriteSteps(builder, diagram.Steps, 1, state);
    }

    /// <summary>Indicates whether the parser reads a title statement back as the title: no line break, semicolon nor comment.</summary>
    private static bool IsTitleStatement(string title)
    {
        if (title.Length is 0 || title.Trim().Length != title.Length || title[0] == ':' || title.Contains("%%", StringComparison.Ordinal))
            return false;

        for (var i = 0; i < title.Length; i++)
        {
            var c = title[i];
            if (c is '\n' or '\r' or ';')
            {
                // A semicolon can end an entity code
                if (c == ';' && i > 0 && IsInEntity(title, i))
                    continue;

                return false;
            }

            if (c == '#' && !IsEntityStart(title, i))
                return false;
        }

        return true;
    }

    private static bool IsEntityStart(string text, int index)
    {
        var end = index + 1;
        while (end < text.Length && char.IsAsciiLetterOrDigit(text[end]))
        {
            end++;
        }

        return end > index + 1 && end < text.Length && text[end] == ';';
    }

    private static bool IsInEntity(string text, int semicolon)
    {
        var start = semicolon - 1;
        while (start >= 0 && char.IsAsciiLetterOrDigit(text[start]))
        {
            start--;
        }

        return start >= 0 && start < semicolon - 1 && text[start] == '#';
    }

    private sealed class WriterState(Dictionary<string, SequenceParticipant> ids, HashSet<SequenceParticipant> declared)
    {
        public Dictionary<string, SequenceParticipant> Ids { get; } = ids;

        public HashSet<SequenceParticipant> Declared { get; } = declared;

        public Dictionary<SequenceParticipant, int> Activations { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private static void WriteSteps(StringBuilder builder, IList<SequenceStep> steps, int indent, WriterState state)
    {
        if (indent > 66)
            throw new ArgumentException("The blocks are nested too deeply.", nameof(steps));

        foreach (var step in steps)
        {
            switch (step)
            {
                case SequenceMessage message:
                    WriteMessage(builder, message, indent, state);
                    break;
                case SequenceNote note:
                    if (note.Participants.Count is 0 or > 2 || (note.Participants.Count == 2 && note.Position is not SequenceNotePosition.Over))
                        throw new ArgumentException("A note is on one participant, or over two in Mermaid.", nameof(steps));

                    Use(note.Participants[0], state);
                    builder.AppendIndent(indent).Append("note ").Append(note.Position switch
                    {
                        SequenceNotePosition.LeftOf => "left of ",
                        SequenceNotePosition.RightOf => "right of ",
                        _ => "over ",
                    }).Append(FormatId(note.Participants[0]));
                    if (note.Participants.Count == 2 && note.Participants[1] != note.Participants[0])
                    {
                        Use(note.Participants[1], state);
                        builder.Append(',').Append(FormatId(note.Participants[1]));
                    }

                    builder.Append(": ").Append(EncodeText(note.Text.Text)).Append('\n');
                    break;
                case SequenceActivation activation:
                    Use(activation.Participant, state);
                    if (activation.IsActivation)
                    {
                        state.Activations[activation.Participant] = state.Activations.GetValueOrDefault(activation.Participant) + 1;
                    }
                    else
                    {
                        Deactivate(activation.Participant, state);
                    }

                    builder.AppendIndent(indent).Append(activation.IsActivation ? "activate " : "deactivate ").Append(FormatId(activation.Participant)).Append('\n');
                    break;
                case SequenceAutonumber autonumber:
                    builder.AppendIndent(indent).Append("autonumber");
                    if (!autonumber.IsEnabled)
                    {
                        builder.Append(" off");
                    }
                    else if (autonumber.Start is { } start)
                    {
                        builder.Append(' ').Append(FormatNumber(start));
                        if (autonumber.Increment is { } increment)
                        {
                            builder.Append(' ').Append(FormatNumber(increment));
                        }
                    }
                    else if (autonumber.Increment is not null)
                    {
                        throw new ArgumentException("An increment of the numbering needs a start in Mermaid.", nameof(steps));
                    }

                    builder.Append('\n');
                    break;
                case SequenceBlock block:
                    WriteBlock(builder, block, indent, state);
                    break;
            }
        }
    }

    private static void WriteBlock(StringBuilder builder, SequenceBlock block, int indent, WriterState state)
    {
        var (keyword, separator) = block.Kind switch
        {
            SequenceBlockKind.Alternative => ("alt", "else"),
            SequenceBlockKind.Optional => ("opt", null),
            SequenceBlockKind.Parallel => ("par", "and"),
            SequenceBlockKind.ParallelOverlap => ("par_over", "and"),
            SequenceBlockKind.Critical => ("critical", "option"),
            SequenceBlockKind.Break => ("break", null),
            SequenceBlockKind.Rectangle => ("rect", null),
            _ => ("loop", (string?)null),
        };
        if (block.Sections.Count > 1 && separator is null)
            throw new ArgumentException($"A '{keyword}' block has one section in Mermaid.", nameof(block));

        builder.AppendIndent(indent).Append(keyword);
        if (block.Kind is SequenceBlockKind.Rectangle)
        {
            if (block.Color is { } color)
            {
                builder.Append(' ').Append(FormatColor(color));
            }
        }
        else if (block.Sections.Count > 0 && block.Sections[0].Label is { IsEmpty: false } label)
        {
            builder.Append(' ').Append(EncodeText(label.Text));
        }

        builder.Append('\n');
        for (var i = 0; i < block.Sections.Count; i++)
        {
            var section = block.Sections[i];
            if (i > 0)
            {
                builder.AppendIndent(indent).Append(separator);
                if (section.Label is { IsEmpty: false } sectionLabel)
                {
                    builder.Append(' ').Append(EncodeText(sectionLabel.Text));
                }

                builder.Append('\n');
            }

            WriteSteps(builder, section.Steps, indent + 1, state);
        }

        builder.AppendIndent(indent).Append("end\n");
    }

    private static void WriteMessage(StringBuilder builder, SequenceMessage message, int indent, WriterState state)
    {
        if (message.CreatesReceiver)
        {
            if (state.Declared.Contains(message.To))
                throw new ArgumentException($"The participant '{message.To.Id}' is used before the message that creates it.", nameof(message));

            Register(message.To, state.Ids);
            state.Declared.Add(message.To);
            builder.AppendIndent(indent).Append("create ");
            WriteDeclaration(builder, message.To, 0, isCreated: true);
        }

        Use(message.From, state);
        Use(message.To, state);
        if (message.DestroyedParticipant is { } destroyed)
        {
            if (destroyed != message.From && destroyed != message.To)
                throw new ArgumentException("A message destroys its sender or its receiver.", nameof(message));

            if (message.CreatesReceiver)
                throw new ArgumentException("A message cannot create and destroy a participant in Mermaid.", nameof(message));

            builder.AppendIndent(indent).Append("destroy ").Append(FormatId(destroyed)).Append('\n');
        }

        var arrow = Array.Find(SequenceDiagramParser.Arrows, item => item.LineStyle == message.LineStyle && item.Start == message.StartArrowHead && item.End == message.EndArrowHead).Text
            ?? throw new ArgumentException("The combination of the decorations of the message cannot be written as Mermaid.", nameof(message));
        var modifiers = (message.ActivatesReceiver ? 1 : 0) + (message.DeactivatesSender ? 1 : 0) + (message.ReceiverConnection ? 1 : 0);
        if (modifiers > 1 || (message.SenderConnection && (message.ActivatesReceiver || message.DeactivatesSender)))
            throw new ArgumentException("A message activates, deactivates or connects in Mermaid, not several of them.", nameof(message));

        if (message.DeactivatesSender)
        {
            Deactivate(message.From, state);
        }

        if (message.ActivatesReceiver)
        {
            state.Activations[message.To] = state.Activations.GetValueOrDefault(message.To) + 1;
        }

        builder.AppendIndent(indent).Append(FormatId(message.From))
            .Append(message.SenderConnection ? "()" : string.Empty)
            .Append(arrow)
            .Append(message.ActivatesReceiver ? "+" : message.DeactivatesSender ? "-" : message.ReceiverConnection ? "()" : string.Empty)
            .Append(FormatId(message.To))
            .Append(':');
        if (message.Text is { IsEmpty: false } text)
        {
            builder.Append(' ').Append(EncodeText(text.Text));
        }

        builder.Append('\n');
    }

    private static void Deactivate(SequenceParticipant participant, WriterState state)
    {
        var count = state.Activations.GetValueOrDefault(participant);
        if (count < 1)
            throw new ArgumentException($"The participant '{participant.Id}' is deactivated while it is not active.", nameof(participant));

        state.Activations[participant] = count - 1;
    }

    /// <summary>Checks that a participant used by a step can be named; a participant that is not declared is declared by its first use.</summary>
    private static void Use(SequenceParticipant participant, WriterState state)
    {
        if (state.Declared.Contains(participant))
            return;

        Register(participant, state.Ids);
        state.Declared.Add(participant);
    }

    private static void Register(SequenceParticipant participant, Dictionary<string, SequenceParticipant> ids)
    {
        if (ids.TryGetValue(participant.Id, out var other) && other != participant)
            throw new ArgumentException($"The identifier '{participant.Id}' is used by several participants.", nameof(participant));

        ids[participant.Id] = participant;
    }

    private static void WriteDeclaration(StringBuilder builder, SequenceParticipant participant, int indent, bool isCreated)
    {
        var hasAlias = participant.Label is { IsEmpty: false } label && !string.Equals(label.Text, participant.Id, StringComparison.Ordinal);
        var hasType = participant.Kind is not (SequenceParticipantKind.Participant or SequenceParticipantKind.Actor);
        if ((hasAlias || hasType) && !SequenceDiagramParser.IsValidId(participant.Id, allowSpaces: false))
            throw new ArgumentException($"The participant '{participant.Id}' has an alias or a type: its identifier cannot contain spaces in Mermaid.", nameof(participant));

        builder.AppendIndent(indent).Append(participant.Kind is SequenceParticipantKind.Actor ? "actor " : "participant ").Append(FormatId(participant));
        if (hasType)
        {
            builder.Append("@{ \"type\": \"").Append(participant.Kind.ToString().ToLowerInvariant()).Append("\" }");
        }

        if (hasAlias)
        {
            builder.Append(" as ").Append(EncodeText(participant.Label!.Text));
        }

        builder.Append('\n');
        _ = isCreated;
    }

    private static string FormatId(SequenceParticipant participant)
    {
        if (!SequenceDiagramParser.IsValidId(participant.Id, allowSpaces: true))
            throw new ArgumentException($"The participant '{participant.Id}' cannot be written as Mermaid.", nameof(participant));

        return participant.Id;
    }

    private static string FormatBox(SequenceBox box)
    {
        var builder = new StringBuilder();
        builder.Append(' ').Append(box.Color is { } color ? FormatColor(color) : "transparent");
        if (box.Title is { IsEmpty: false } title)
        {
            // The color is a function up to its last parenthesis for Mermaid: the parentheses of the title are entity codes
            builder.Append(' ').Append(EncodeText(title.Text).Replace("(", "#40;", StringComparison.Ordinal).Replace(")", "#41;", StringComparison.Ordinal));
        }

        return builder.ToString();
    }

    /// <summary>Writes a color as a function, since <c>#</c> starts a comment in a sequence diagram.</summary>
    private static string FormatColor(SceneColor color) => color.A == 255
        ? string.Create(CultureInfo.InvariantCulture, $"rgb({color.R}, {color.G}, {color.B})")
        : string.Create(CultureInfo.InvariantCulture, $"rgba({color.R}, {color.G}, {color.B}, {Math.Round(color.A / 255.0, 3, MidpointRounding.AwayFromZero)})");

    private static string FormatNumber(decimal value)
    {
        if (value < 0 || decimal.Round(value, 2, MidpointRounding.AwayFromZero) != value)
            throw new ArgumentException($"The number {value} cannot be written as Mermaid: it is negative or has more than two decimals.", nameof(value));

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Encodes a text that ends at a semicolon or a <c>#</c>: these characters become entity codes, and a text that starts
    /// with the wrap prefix of Mermaid starts with an entity code.
    /// </summary>
    internal static string EncodeText(string text)
    {
        var encoded = MermaidText.EncodePlain(text.Trim()).Replace(";", "#59;", StringComparison.Ordinal);

        // EncodePlain turned ; of its own codes into #59; too: put them back
        encoded = RestoreCodes(encoded);
        if (encoded.StartsWith("wrap:", StringComparison.Ordinal) || encoded.StartsWith("nowrap:", StringComparison.Ordinal) || encoded.StartsWith(":wrap:", StringComparison.Ordinal) || encoded.StartsWith(":nowrap:", StringComparison.Ordinal))
        {
            encoded = "#" + ((int)encoded[0]).ToString(CultureInfo.InvariantCulture) + ";" + encoded[1..];
        }

        return encoded;
    }

    private static string RestoreCodes(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '#')
            {
                var end = i + 1;
                while (end < text.Length && char.IsAsciiLetterOrDigit(text[end]))
                {
                    end++;
                }

                if (end > i + 1 && string.CompareOrdinal(text, end, "#59;", 0, 4) == 0)
                {
                    builder.Append(text, i, end - i).Append(';');
                    i = end + 3;
                    continue;
                }
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }
}
