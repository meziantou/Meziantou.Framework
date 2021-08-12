namespace Meziantou.Framework.CodeDom;

public class SnippetExpression : Expression
{
    public SnippetExpression()
    {
    }

    public SnippetExpression(string? expression)
    {
        Expression = expression;
    }

    public string? Expression { get; set; }
}
