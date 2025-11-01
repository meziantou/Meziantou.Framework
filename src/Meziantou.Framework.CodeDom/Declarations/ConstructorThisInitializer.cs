namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a this constructor initializer (: this()).</summary>
public class ConstructorThisInitializer : ConstructorInitializer
{
    public ConstructorThisInitializer()
    {
    }

    public ConstructorThisInitializer(params Expression[] codeExpressions) : base(codeExpressions)
    {
    }

    public ConstructorThisInitializer(IEnumerable<Expression>? codeExpressions) : base(codeExpressions)
    {
    }
}
