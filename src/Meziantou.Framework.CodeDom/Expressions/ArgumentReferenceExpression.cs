namespace Meziantou.Framework.CodeDom;

public class ArgumentReferenceExpression : Expression
{
    private MethodArgumentDeclaration? _argumentDeclaration;
    private string? _name;

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

            return _name;
        }
        set
        {
            _name = value;
            _argumentDeclaration = null;
        }
    }

    public static implicit operator ArgumentReferenceExpression(MethodArgumentDeclaration methodArgumentDeclaration) => new(methodArgumentDeclaration);
}
