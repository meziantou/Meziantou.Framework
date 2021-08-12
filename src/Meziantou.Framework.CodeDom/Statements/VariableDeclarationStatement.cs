namespace Meziantou.Framework.CodeDom;

public class VariableDeclarationStatement : Statement
{
    private Expression? _initExpression;

    public VariableDeclarationStatement()
    {
    }

    public VariableDeclarationStatement(string? name, TypeReference? type, Expression? initExpression = null)
    {
        Type = type;
        Name = name;
        InitExpression = initExpression;
    }

    public string? Name { get; set; }

    public TypeReference? Type { get; set; }

    public Expression? InitExpression
    {
        get => _initExpression;
        set => SetParent(ref _initExpression, value);
    }
}

