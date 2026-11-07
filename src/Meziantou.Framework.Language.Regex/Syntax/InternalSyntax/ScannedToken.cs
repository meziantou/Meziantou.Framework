using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>A token the scanner has just read, together with where it read it from.</summary>
/// <remarks>
/// <para>
/// An immutable token knows its width but not its position, which is what lets one stand at more than one place. A
/// parser, though, reports diagnostics against the pattern it is reading, and needs to say where. So the scanner
/// hands back both: the token to put in the tree, and the span it came out of.
/// </para>
/// <para>
/// <see langword="default"/> is the token that is not there, which is what an optional part of a construct gets when
/// the pattern does not contain it.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Green,nq} {Span}")]
internal readonly struct ScannedToken
{
    public ScannedToken(GreenToken green, int start, int end)
    {
        Green = green;
        Start = start;
        End = end;
    }

    /// <summary>The token itself, or <see langword="null"/> when the pattern did not contain one.</summary>
    public GreenToken? Green { get; }

    /// <summary>Gets a value indicating whether the pattern contained this token at all.</summary>
    public bool IsPresent => Green is not null;

    /// <summary>Where the token's own text starts.</summary>
    public int Start { get; }

    /// <summary>Where the token's own text ends.</summary>
    public int End { get; }

    /// <summary>The range the token's own text covers, not counting the trivia in front of it.</summary>
    public TextSpan Span => TextSpan.FromBounds(Start, End);

    /// <summary>The range the token covers, counting the trivia in front of it.</summary>
    public TextSpan FullSpan => TextSpan.FromBounds(Start - (Green?.GetLeadingTriviaWidth() ?? 0), End);

    public string Text => Green is { } green ? green.Text : "";

    public string ValueText => Green is { } green ? green.ValueText : "";

    public bool IsMissing => Green?.IsMissing ?? false;

    public SyntaxKind Kind => (SyntaxKind)(Green?.RawKind ?? 0);

    /// <summary>Unwraps the token so it can be put straight into a node.</summary>
    /// <remarks>
    /// The result is only null for a token the pattern did not contain, and those are only ever passed to the slots
    /// that accept one. Saying so here rather than at every construction site is what keeps the parsers readable.
    /// </remarks>
    public static implicit operator GreenNode(ScannedToken token) => token.Green!;

    public override string ToString() => Text;
}
