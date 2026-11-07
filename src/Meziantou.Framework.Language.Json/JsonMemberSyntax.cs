using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>One member of an object: a name, a colon, and a value.</summary>
/// <remarks>The comma after a member is not part of it: it separates the members of the object's list.</remarks>
public sealed class JsonMemberSyntax : JsonSyntaxNode
{
    private JsonValueSyntax? _value;

    internal JsonMemberSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken NameToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the name of the member, with its quotes and escapes resolved.</summary>
    public string Name => NameToken.ValueText;

    public SyntaxToken ColonToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    public JsonValueSyntax Value => GetRed(ref _value, 2)!;

    /// <summary>Returns this member with the given parts, or itself when nothing changed.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public JsonMemberSyntax Update(SyntaxToken nameToken, SyntaxToken colonToken, JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (nameToken.Node == Green.GetSlot(0) && colonToken.Node == Green.GetSlot(1) && ReferenceEquals(value.Green, Green.GetSlot(2)))
            return this;

        return SyntaxFactory.JsonMember(nameToken, colonToken, value).WithAnnotationsFrom(this);
    }

    public JsonMemberSyntax WithNameToken(SyntaxToken nameToken) => Update(nameToken, ColonToken, Value);

    /// <summary>Returns this member with a new name, escaped as JSON requires.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public JsonMemberSyntax WithName(string name) => WithNameToken(SyntaxFactory.Literal(name).WithTriviaFrom(NameToken));

    public JsonMemberSyntax WithColonToken(SyntaxToken colonToken) => Update(NameToken, colonToken, Value);
    public JsonMemberSyntax WithValue(JsonValueSyntax value) => Update(NameToken, ColonToken, value);

    internal override SyntaxNode? GetNodeSlot(int index) => index == 2 ? GetRed(ref _value, 2) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 2 ? _value : null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonMember(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonMember(this);
    }
}
