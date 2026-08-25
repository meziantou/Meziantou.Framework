namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a simple command: optional assignments, a command name, arguments, and redirections.</summary>
/// <remarks>
/// <see cref="ChildNodes"/> keeps the parts in source order, so a command such as <c>echo &gt;out hi</c> round-trips
/// exactly. Use <see cref="Name"/>, <see cref="Arguments"/>, <see cref="Assignments"/>, and <see cref="Redirections"/>
/// to read the parts by role.
/// </remarks>
public sealed class ShellCommandSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellCommandSyntax(IReadOnlyList<ShellSyntaxNode>? elements)
        : base(ShellSyntaxKind.Command, BuildFullText(elements ?? []), GetFullStart(elements))
    {
        _childNodes = elements ?? [];
        Assignments = [.. _childNodes.OfType<ShellAssignmentSyntax>()];
        Redirections = [.. _childNodes.OfType<ShellRedirectionSyntax>()];

        var words = _childNodes.OfType<ShellWordSyntax>().ToArray();
        Name = words.Length > 0 ? words[0] : null;
        Arguments = words.Length > 1 ? words[1..] : [];
    }

    /// <summary>The command name, or <see langword="null"/> for an assignment-only or redirection-only command.</summary>
    public ShellWordSyntax? Name { get; }

    public IReadOnlyList<ShellWordSyntax> Arguments { get; }

    /// <summary>The assignments that prefix the command, as in <c>FOO=bar cmd</c>.</summary>
    public IReadOnlyList<ShellAssignmentSyntax> Assignments { get; }

    public IReadOnlyList<ShellRedirectionSyntax> Redirections { get; }

    /// <summary>The assignments, words, and redirections of this command, in source order.</summary>
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The command name with quotes and escapes resolved, or <see langword="null"/> when it needs runtime expansion.</summary>
    public string? NameValue => Name?.Value;

    public ShellCommandSyntax WithChildNodes(IEnumerable<ShellSyntaxNode>? elements)
    {
        var updated = elements?.ToArray() ?? [];
        if (updated.SequenceEqual(ChildNodes))
            return this;

        return new ShellCommandSyntax(updated);
    }

    /// <summary>Replaces the arguments while keeping the command name, assignments, and redirections in place.</summary>
    public ShellCommandSyntax WithArguments(IEnumerable<ShellWordSyntax>? arguments)
    {
        var updated = arguments?.ToArray() ?? [];
        if (updated.SequenceEqual(Arguments))
            return this;

        var elements = new List<ShellSyntaxNode>(ChildNodes.Count);
        var seenName = false;
        var inserted = false;
        foreach (var child in ChildNodes)
        {
            if (child is not ShellWordSyntax)
            {
                elements.Add(child);
                continue;
            }

            if (!seenName)
            {
                seenName = true;
                elements.Add(child);
                continue;
            }

            if (!inserted)
            {
                inserted = true;
                elements.AddRange(updated);
            }
        }

        if (!inserted)
        {
            elements.AddRange(updated);
        }

        return new ShellCommandSyntax(elements);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCommand(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCommand(this);

    private static int GetFullStart(IReadOnlyList<ShellSyntaxNode>? elements) => elements is { Count: > 0 } ? elements[0].FullSpan.Start : 0;
}
