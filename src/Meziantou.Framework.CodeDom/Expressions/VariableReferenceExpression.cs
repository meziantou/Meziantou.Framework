namespace Meziantou.Framework.CodeDom;

public class VariableReferenceExpression : Expression
{
    private VariableDeclarationStatement? _variableDeclarationStatement;

    public VariableReferenceExpression()
    {
    }

    public VariableReferenceExpression(VariableDeclarationStatement variableDeclarationStatement)
    {
        _variableDeclarationStatement = variableDeclarationStatement;
    }

    public VariableReferenceExpression(string name)
    {
        Name = name;
    }

    public string? Name
    {
        get
        {
            if (_variableDeclarationStatement is not null)
                return _variableDeclarationStatement.Name;

            return field;
        }
        set
        {
            field = value;
            _variableDeclarationStatement = null;
        }
    }

    public static implicit operator VariableReferenceExpression(VariableDeclarationStatement variableDeclarationStatement) => new(variableDeclarationStatement);
}
