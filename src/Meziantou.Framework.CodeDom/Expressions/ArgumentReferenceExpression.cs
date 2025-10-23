namespace Meziantou.Framework.CodeDom;

public class ArgumentReferenceExpression : Expression
{
    private MethodArgumentDeclaration? _argumentDeclaration;

    public ArgumentReferenceExpression()
    {
    }

    public ArgumentReferenceExpression(MethodArgumentDeclaration argumentDeclaration)
    {
        _argumentDeclaration = argumentDeclaration;
    }

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
