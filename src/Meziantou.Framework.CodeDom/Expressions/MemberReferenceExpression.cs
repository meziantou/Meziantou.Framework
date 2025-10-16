namespace Meziantou.Framework.CodeDom;

public class MemberReferenceExpression : Expression
{
    private MemberDeclaration? _memberDeclaration;

    public MemberReferenceExpression()
    {
    }

    public MemberReferenceExpression(MemberDeclaration? memberDeclaration)
    {
        TargetObject = new ThisExpression();
        _memberDeclaration = memberDeclaration;
    }

    public MemberReferenceExpression(Expression? targetObject, MemberDeclaration? memberDeclaration)
    {
        TargetObject = targetObject;
        _memberDeclaration = memberDeclaration;
    }

    public MemberReferenceExpression(Expression? targetObject, string? memberName)
    {
        TargetObject = targetObject;
        Name = memberName;
    }

    public MemberReferenceExpression(TypeReference? type, string? memberName)
    {
        TargetObject = type is not null ? new TypeReferenceExpression(type) : null;
        Name = memberName;
    }

    public string? Name
    {
        get
        {
            if (_memberDeclaration is not null)
                return _memberDeclaration.Name;

            return field;
        }
        set
        {
            field = value;
            _memberDeclaration = null;
        }
    }

    public Expression? TargetObject
    {
        get;
        set => SetParent(ref field, value);
    }
}
