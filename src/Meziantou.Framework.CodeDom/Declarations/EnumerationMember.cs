namespace Meziantou.Framework.CodeDom;

public class EnumerationMember : MemberDeclaration
{
    public EnumerationMember()
    {
    }

    public EnumerationMember(string? name)
    {
        Name = name;
    }

    public EnumerationMember(string? name, Expression value)
    {
        Name = name;
        Value = value;
    }

    public Expression? Value
    {
        get;
        set => SetParent(ref field, value);
    }
}
