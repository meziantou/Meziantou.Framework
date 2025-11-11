namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a method invocation expression.</summary>
/// <example>
/// <code>
/// var method = new MemberReferenceExpression(typeof(Console), "WriteLine");
/// var invoke = new MethodInvokeExpression(method, new LiteralExpression("Hello World"));
/// </code>
/// </example>
public class MethodInvokeExpression : Expression
{
    public MethodInvokeExpression()
        : this(method: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MethodInvokeExpression"/> class with the specified method.</summary>
    /// <param name="method">The method to invoke.</param>
    public MethodInvokeExpression(Expression? method)
        : this(method, parameters: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MethodInvokeExpression"/> class with the specified method and arguments.</summary>
    /// <param name="method">The method to invoke.</param>
    /// <param name="arguments">The method arguments.</param>
    public MethodInvokeExpression(Expression? method, params Expression[] arguments)
        : this(method, parameters: null, arguments)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MethodInvokeExpression"/> class with the specified method, type parameters, and arguments.</summary>
    /// <param name="method">The method to invoke.</param>
    /// <param name="parameters">The generic type parameters for the method.</param>
    /// <param name="arguments">The method arguments.</param>
    public MethodInvokeExpression(Expression? method, TypeReference[]? parameters, params Expression[] arguments)
    {
        Parameters = new List<TypeReference>();
        Arguments = new CodeObjectCollection<Expression>(this);
        Method = method;
        Arguments.AddRange(arguments);
        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                Parameters.Add(parameter);
            }
        }
    }

    public Expression? Method
    {
        get;
        set => SetParent(ref field, value);
    }

    public CodeObjectCollection<Expression> Arguments { get; }

    public IList<TypeReference> Parameters { get; }
}
