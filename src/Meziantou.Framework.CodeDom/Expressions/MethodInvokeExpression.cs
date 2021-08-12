using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom;

public class MethodInvokeExpression : Expression
{
    private Expression? _method;

    public MethodInvokeExpression()
        : this(method: null)
    {
    }

    public MethodInvokeExpression(Expression? method)
        : this(method, parameters: null)
    {
    }

    public MethodInvokeExpression(Expression? method, params Expression[] arguments)
        : this(method, parameters: null, arguments)
    {
    }

    public MethodInvokeExpression(Expression? method, TypeReference[]? parameters, params Expression[] arguments)
    {
        Parameters = new List<TypeReference>();
        Arguments = new CodeObjectCollection<Expression>(this);
        Method = method;
        Arguments.AddRange(arguments);
        if (parameters != null)
        {
            foreach (var parameter in parameters)
            {
                Parameters.Add(parameter);
            }
        }
    }

    public Expression? Method
    {
        get => _method;
        set => SetParent(ref _method, value);
    }

    public CodeObjectCollection<Expression> Arguments { get; }

    public IList<TypeReference> Parameters { get; }
}
