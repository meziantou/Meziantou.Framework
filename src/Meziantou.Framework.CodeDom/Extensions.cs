namespace Meziantou.Framework.CodeDom;

public static class Extensions
{
    public static T? SelfOrAnscestorOfType<T>(this CodeObject? codeObject) where T : CodeObject
    {
        while (codeObject is not null)
        {
            if (codeObject is T o)
            {
                return o;
            }

            codeObject = codeObject.Parent;
        }

        return null;
    }

    public static T? AnscestorOfType<T>(this CodeObject? codeObject) where T : CodeObject
    {
        if (codeObject is null)
            return null;

        codeObject = codeObject.Parent;
        while (codeObject is not null)
        {
            if (codeObject is T o)
            {
                return o;
            }

            codeObject = codeObject.Parent;
        }

        return null;
    }

    public static NamespaceDeclaration AddNamespace(this INamespaceDeclarationContainer unit, string name)
    {
        var ns = new NamespaceDeclaration(name);
        unit.Namespaces.Add(ns);
        return ns;
    }

    public static NamespaceDeclaration AddNamespace(this INamespaceDeclarationContainer unit, NamespaceDeclaration ns)
    {
        unit.Namespaces.Add(ns);
        return ns;
    }

    public static T AddType<T>(this ITypeDeclarationContainer unit, T type) where T : TypeDeclaration
    {
        unit.Types.Add(type);
        return type;
    }

    public static UsingDirective AddUsing(this IUsingDirectiveContainer unit, string ns)
    {
        var directive = new UsingDirective(ns);
        unit.Usings.Add(directive);
        return directive;
    }

    public static MethodArgumentDeclaration AddArgument(this MethodDeclaration method, MethodArgumentDeclaration argument)
    {
        method.Arguments.Add(argument);
        return argument;
    }

    public static MethodArgumentDeclaration AddArgument(this MethodDeclaration method, string name, TypeReference type)
    {
        var argument = new MethodArgumentDeclaration(type, name);
        method.Arguments.Add(argument);
        return argument;
    }

    public static MethodArgumentDeclaration AddArgument(this MethodDeclaration method, string name, TypeReference type, Direction direction)
    {
        var argument = new MethodArgumentDeclaration(type, name)
        {
            Direction = direction,
        };

        method.Arguments.Add(argument);
        return argument;
    }

    public static T AddMember<T>(this IMemberContainer c, T member) where T : MemberDeclaration
    {
        c.Members.Add(member);
        return member;
    }

    public static ConditionStatement CreateThrowIfNullStatement(this MethodArgumentDeclaration argument)
    {
        return new ConditionStatement
        {
            Condition = new BinaryExpression(BinaryOperator.Equals, argument, new LiteralExpression(value: null)),
            TrueStatements = new ThrowStatement(new NewObjectExpression(typeof(ArgumentNullException), new NameofExpression(argument))),
        };
    }

    public static MethodInvokeExpression InvokeMethod(this Expression expression, params Expression[] arguments) => new(expression, arguments);

    public static MethodInvokeExpression InvokeMethod(this Expression expression, TypeReference[] parameters, params Expression[] arguments) => new(expression, parameters, arguments);

    public static MethodInvokeExpression InvokeMethod(this VariableDeclarationStatement expression, params Expression[] arguments) => InvokeMethod(new VariableReferenceExpression(expression), arguments);

    public static MethodInvokeExpression InvokeMethod(this VariableDeclarationStatement expression, TypeReference[] parameters, params Expression[] arguments) => InvokeMethod(new VariableReferenceExpression(expression), parameters, arguments);

    public static MemberReferenceExpression Member(this Expression expression, string name, params string[] names)
    {
        var result = new MemberReferenceExpression(expression, name);
        foreach (var n in names)
        {
            result = new MemberReferenceExpression(result, n);
        }

        return result;
    }

    public static MemberReferenceExpression Member(this VariableDeclarationStatement variable, string name, params string[] names) => Member(new VariableReferenceExpression(variable), name, names);

    public static MemberReferenceExpression Member(this MethodArgumentDeclaration argument, string name, params string[] names) => Member(new ArgumentReferenceExpression(argument), name, names);
    public static MemberReferenceExpression Member(this FieldDeclaration field, string name, params string[] names) => Member(new MemberReferenceExpression(field), name, names);
    public static MemberReferenceExpression Member(this PropertyDeclaration prop, string name, params string[] names) => Member(new MemberReferenceExpression(prop), name, names);
}
