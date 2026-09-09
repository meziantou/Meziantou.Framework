using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>An object: braces around a comma-separated list of members.</summary>
public sealed class JsonObjectSyntax : JsonValueSyntax
{
    private SyntaxNode? _members;

    internal JsonObjectSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBraceToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the members of the object. The commas between them are the separators of the list.</summary>
    public SeparatedSyntaxList<JsonMemberSyntax> Members
    {
        get
        {
            var red = GetRed(ref _members, 1);

            return red is null ? default : new SeparatedSyntaxList<JsonMemberSyntax>(new SyntaxNodeOrTokenList(red, GetChildIndex(1)));
        }
    }

    public SyntaxToken CloseBraceToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the first member called <paramref name="name"/>, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public JsonMemberSyntax? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var member in Members)
        {
            if (string.Equals(member.Name, name, StringComparison.Ordinal))
                return member;
        }

        return null;
    }

    /// <summary>Returns this object with the given parts, or itself when nothing changed.</summary>
    public JsonObjectSyntax Update(SyntaxToken openBraceToken, SeparatedSyntaxList<JsonMemberSyntax> members, SyntaxToken closeBraceToken)
    {
        if (openBraceToken.Node == Green.GetSlot(0) && members.Green == Green.GetSlot(1) && closeBraceToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.JsonObject(openBraceToken, members, closeBraceToken).WithAnnotationsFrom(this);
    }

    public JsonObjectSyntax WithOpenBraceToken(SyntaxToken openBraceToken) => Update(openBraceToken, Members, CloseBraceToken);
    public JsonObjectSyntax WithMembers(SeparatedSyntaxList<JsonMemberSyntax> members) => Update(OpenBraceToken, members, CloseBraceToken);
    public JsonObjectSyntax WithCloseBraceToken(SyntaxToken closeBraceToken) => Update(OpenBraceToken, Members, closeBraceToken);
    public JsonObjectSyntax AddMembers(params JsonMemberSyntax[] items) => WithMembers(Members.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 1 ? GetRed(ref _members, 1) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 1 ? _members : null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonObject(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonObject(this);
    }
}
