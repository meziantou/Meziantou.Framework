namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a simple command: optional assignments, a command name, arguments, and redirections.</summary>
/// <remarks>
/// <see cref="Elements"/> keeps the parts in source order, so a command such as <c>echo &gt;out hi</c> round-trips
/// exactly. Use <see cref="Name"/>, <see cref="Arguments"/>, <see cref="Assignments"/>, and <see cref="Redirections"/>
/// to read the parts by role.
/// </remarks>
public sealed partial class ShellCommandSyntax
{
    /// <summary>The command name, or <see langword="null"/> for an assignment-only or redirection-only command.</summary>
    public ShellWordSyntax? Name => Elements.OfType<ShellWordSyntax>().FirstOrDefault();

    /// <summary>Every word after the command name.</summary>
    public IReadOnlyList<ShellWordSyntax> Arguments => [.. Elements.OfType<ShellWordSyntax>().Skip(1)];

    /// <summary>The assignments that prefix the command, as in <c>FOO=bar cmd</c>.</summary>
    public IReadOnlyList<ShellAssignmentSyntax> Assignments => [.. Elements.OfType<ShellAssignmentSyntax>()];

    public IReadOnlyList<ShellRedirectionSyntax> Redirections => [.. Elements.OfType<ShellRedirectionSyntax>()];

    /// <summary>Replaces the arguments while keeping the command name, assignments, and redirections in place.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="arguments"/> is <see langword="null"/>.</exception>
    public ShellCommandSyntax WithArguments(IEnumerable<ShellWordSyntax> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // A word built by SyntaxFactory carries no trivia, so without a separator it would run into the command name
        // or the word before it. Words that bring their own leading trivia keep the spacing they were given.
        var replacements = arguments.Select(SyntaxFactory.WithLeadingSpace).ToArray();

        var elements = new List<ShellSyntaxNode>(Elements.Count);
        var seenName = false;
        var inserted = false;
        foreach (var child in Elements)
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
                elements.AddRange(replacements);
            }
        }

        if (!inserted)
        {
            elements.AddRange(replacements);
        }

        return WithElements(new SyntaxList<ShellSyntaxNode>(elements));
    }

    /// <summary>The command name with quotes and escapes resolved, or <see langword="null"/> when it needs runtime expansion.</summary>
    public string? NameValue => Name?.Value;
}
