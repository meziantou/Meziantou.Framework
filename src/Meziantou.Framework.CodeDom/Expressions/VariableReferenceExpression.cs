namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a reference to a local variable.</summary>
public class VariableReferenceExpression : Expression
{
    private VariableDeclarationStatement? _variableDeclarationStatement;

    public VariableReferenceExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VariableReferenceExpression"/> class referencing a variable declaration.</summary>
    /// <param name="variableDeclarationStatement">The variable declaration to reference.</param>
    public VariableReferenceExpression(VariableDeclarationStatement variableDeclarationStatement)
    {
        _variableDeclarationStatement = variableDeclarationStatement;
    }

    /// <summary>Initializes a new instance of the <see cref="VariableReferenceExpression"/> class with the specified variable name.</summary>
    /// <param name="name">The variable name.</param>
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
