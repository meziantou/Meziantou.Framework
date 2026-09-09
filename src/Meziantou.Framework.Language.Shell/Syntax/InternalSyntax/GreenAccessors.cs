using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>
/// The few things the parsers ask a node about while they are still building the tree.
/// </summary>
/// <remarks>
/// A red node answers these from its slots, but the parsers only have green nodes in hand, so the same questions are
/// answered here from the same slots.
/// </remarks>
internal static class GreenAccessors
{
    /// <summary>How many parts a word has, without materialising them.</summary>
    public static int PartCount(this ShellWordSyntax word) => SlotCount(word.GetSlot(0));

    /// <summary>How many parts a quoted string has.</summary>
    public static int PartCount(this ShellQuotedStringSyntax quoted) => SlotCount(quoted.GetSlot(1));

    /// <summary>The first part of a quoted string, or <see langword="null"/> when it has none.</summary>
    public static GreenNode? FirstPart(this ShellQuotedStringSyntax quoted) => FirstSlot(quoted.GetSlot(1));

    /// <summary>The operator that introduces a redirection.</summary>
    public static GreenToken? OperatorToken(this ShellRedirectionSyntax redirection) => redirection.GetSlot(1) as GreenToken;

    /// <summary>The <c>;;</c> or similar that ends a case clause, or <see langword="null"/> when the source omitted it.</summary>
    public static GreenToken? TerminatorToken(this PosixCaseClauseSyntax clause) => clause.GetSlot(4) as GreenToken;

    /// <summary>The text a word resolves to, or <see langword="null"/> when it needs runtime expansion.</summary>
    public static string? WordValue(this ShellWordSyntax word)
    {
        var builder = new StringBuilder();
        foreach (var part in Parts(word.GetSlot(0)))
        {
            switch (part)
            {
                case ShellLiteralWordPartSyntax or ShellEscapeSequenceSyntax or ShellGlobSyntax:
                    builder.Append((part.GetSlot(0) as GreenToken)?.ValueText);
                    break;

                case ShellQuotedStringSyntax quoted:
                    foreach (var inner in Parts(quoted.GetSlot(1)))
                    {
                        if (inner is not (ShellLiteralWordPartSyntax or ShellEscapeSequenceSyntax))
                            return null;

                        builder.Append((inner.GetSlot(0) as GreenToken)?.ValueText);
                    }

                    break;

                default:
                    return null;
            }
        }

        return builder.ToString();
    }

    private static int SlotCount(GreenNode? list) => list is null ? 0 : list.IsList ? list.SlotCount : 1;

    private static GreenNode? FirstSlot(GreenNode? list) => list is null ? null : list.IsList ? list.GetSlot(0) : list;

    private static IEnumerable<GreenNode> Parts(GreenNode? list)
    {
        if (list is null)
            yield break;

        if (!list.IsList)
        {
            yield return list;
            yield break;
        }

        for (var index = 0; index < list.SlotCount; index++)
        {
            if (list.GetSlot(index) is { } slot)
            {
                yield return slot;
            }
        }
    }
}
