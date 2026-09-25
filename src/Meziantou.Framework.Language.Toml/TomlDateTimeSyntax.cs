using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A date, a time, or both. Which one is the node's kind.</summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Kind</term><description>Example, and the type of <see cref="Value"/></description></listheader>
/// <item><term><see cref="SyntaxKind.TomlOffsetDateTime"/></term><description><c>1979-05-27T07:32:00Z</c>, a <see cref="DateTimeOffset"/></description></item>
/// <item><term><see cref="SyntaxKind.TomlLocalDateTime"/></term><description><c>1979-05-27T07:32:00</c>, a <see cref="DateTime"/> of kind <see cref="DateTimeKind.Unspecified"/></description></item>
/// <item><term><see cref="SyntaxKind.TomlLocalDate"/></term><description><c>1979-05-27</c>, a <see cref="DateOnly"/></description></item>
/// <item><term><see cref="SyntaxKind.TomlLocalTime"/></term><description><c>07:32:00</c>, a <see cref="TimeOnly"/></description></item>
/// </list>
/// <para>
/// Digits past the seventh of a fraction of a second are truncated, as the specification asks. An offset further from
/// UTC than .NET allows (±14:00) gives the same instant with an offset of zero.
/// </para>
/// </remarks>
public sealed class TomlDateTimeSyntax : TomlValueSyntax
{
    internal TomlDateTimeSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken DateTimeToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the value: a <see cref="DateTimeOffset"/>, <see cref="DateTime"/>, <see cref="DateOnly"/>, or <see cref="TimeOnly"/>, depending on the kind.</summary>
    /// <exception cref="InvalidOperationException">The token was built by hand and does not spell a date or a time.</exception>
    public object Value => DateTimeToken.Value is { } value and (DateTimeOffset or DateTime or DateOnly or TimeOnly) ? value : Internals.TokenValues.ParseDateTime(DateTimeToken.Text);

    /// <summary>Returns this value with a different token, or itself when nothing changed.</summary>
    /// <remarks>The kind of the node follows the token, so replacing a date with a time changes both.</remarks>
    public TomlDateTimeSyntax Update(SyntaxToken dateTimeToken)
    {
        if (dateTimeToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.TomlDateTime(dateTimeToken).WithAnnotationsFrom(this);
    }

    public TomlDateTimeSyntax WithDateTimeToken(SyntaxToken dateTimeToken) => Update(dateTimeToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlDateTime(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlDateTime(this);
    }
}
