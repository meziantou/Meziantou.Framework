namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a reference to a member (field, property, method) of an object or type.</summary>
/// <example>
/// <code>
/// var memberRef = new MemberReferenceExpression(new ThisExpression(), "Name");
/// var staticMemberRef = new MemberReferenceExpression(typeof(Console), "WriteLine");
/// </code>
/// </example>
public class MemberReferenceExpression : Expression
{
    private MemberDeclaration? _memberDeclaration;

    public MemberReferenceExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MemberReferenceExpression"/> class referencing a member declaration.</summary>
    /// <param name="memberDeclaration">The member declaration to reference.</param>
    public MemberReferenceExpression(MemberDeclaration? memberDeclaration)
    {
        TargetObject = new ThisExpression();
        _memberDeclaration = memberDeclaration;
    }

    /// <summary>Initializes a new instance of the <see cref="MemberReferenceExpression"/> class with a target object and member declaration.</summary>
    /// <param name="targetObject">The target object expression.</param>
    /// <param name="memberDeclaration">The member declaration to reference.</param>
    public MemberReferenceExpression(Expression? targetObject, MemberDeclaration? memberDeclaration)
    {
        TargetObject = targetObject;
        _memberDeclaration = memberDeclaration;
    }

    /// <summary>Initializes a new instance of the <see cref="MemberReferenceExpression"/> class with a target object and member name.</summary>
    /// <param name="targetObject">The target object expression.</param>
    /// <param name="memberName">The member name.</param>
    public MemberReferenceExpression(Expression? targetObject, string? memberName)
    {
        TargetObject = targetObject;
        Name = memberName;
    }

    /// <summary>Initializes a new instance of the <see cref="MemberReferenceExpression"/> class referencing a static member.</summary>
    /// <param name="type">The type containing the member.</param>
    /// <param name="memberName">The member name.</param>
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
