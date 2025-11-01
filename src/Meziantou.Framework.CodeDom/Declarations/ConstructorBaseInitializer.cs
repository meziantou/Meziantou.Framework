namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a base class constructor initializer (: base()).</summary>
public class ConstructorBaseInitializer : ConstructorInitializer
{
    public ConstructorBaseInitializer()
    {
    }

    public ConstructorBaseInitializer(params Expression[] codeExpressions) : base(codeExpressions)
    {
    }

    public ConstructorBaseInitializer(IEnumerable<Expression> codeExpressions) : base(codeExpressions)
    {
    }
}
