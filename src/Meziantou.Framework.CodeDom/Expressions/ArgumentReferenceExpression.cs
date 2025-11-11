namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a reference to a method or constructor argument.</summary>
public class ArgumentReferenceExpression : Expression
{
    private MethodArgumentDeclaration? _argumentDeclaration;

    public ArgumentReferenceExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ArgumentReferenceExpression"/> class referencing an argument declaration.</summary>
    /// <param name="argumentDeclaration">The argument declaration to reference.</param>
    public ArgumentReferenceExpression(MethodArgumentDeclaration argumentDeclaration)
    {
        _argumentDeclaration = argumentDeclaration;
    }

    /// <summary>Initializes a new instance of the <see cref="ArgumentReferenceExpression"/> class with the specified argument name.</summary>
    /// <param name="name">The argument name.</param>
    public ArgumentReferenceExpression(string name)
    {
        Name = name;
    }

    public string? Name
    {
        get
        {
            if (_argumentDeclaration is not null)
                return _argumentDeclaration.Name;

            return field;
        }
        set
        {
            field = value;
            _argumentDeclaration = null;
        }
    }

    public static implicit operator ArgumentReferenceExpression(MethodArgumentDeclaration methodArgumentDeclaration) => new(methodArgumentDeclaration);
}
