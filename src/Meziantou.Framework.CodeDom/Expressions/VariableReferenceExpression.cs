namespace Meziantou.Framework.CodeDom;

public class VariableReferenceExpression : Expression
{
    private VariableDeclarationStatement? _variableDeclarationStatement;
    private string? _name;

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
            if (_variableDeclarationStatement != null)
                return _variableDeclarationStatement.Name;

            return _name;
        }
        set
        {
            _name = value;
            _variableDeclarationStatement = null;
        }
    }

    public static implicit operator VariableReferenceExpression(VariableDeclarationStatement variableDeclarationStatement) => new(variableDeclarationStatement);
}
