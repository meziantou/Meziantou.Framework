using System;

namespace Meziantou.Framework.CodeDom
{
    public static class Extensions
    {
        public static T GetSelfOrParentOfType<T>(this CodeObject codeObject) where T : CodeObject
        {
            while (codeObject != null)
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

        public static T AddMember<T>(this ClassDeclaration c, T member) where T : MemberDeclaration
        {
            c.Members.Add(member);
            return member;
        }

        public static ConditionStatement CreateThrowIfNullStatement(this MethodArgumentDeclaration argument)
        {

            return new ConditionStatement
            {
                Condition = new BinaryExpression(BinaryOperator.Equals, argument, new LiteralExpression(null)),
                TrueStatements = new ThrowStatement(new NewObjectExpression(typeof(ArgumentNullException), new NameofExpression(argument)))
            };
        }

        public static MethodInvokeExpression InvokeMethod(this Expression expression, params Expression[] arguments) => new MethodInvokeExpression(expression, arguments);

        public static MethodInvokeExpression InvokeMethod(this Expression expression, string memberName, params Expression[] arguments) => new MethodInvokeExpression(new MemberReferenceExpression(expression, memberName), arguments);

        public static MethodInvokeExpression InvokeMethod(this Expression expression, TypeReference[] parameters, params Expression[] arguments) => new MethodInvokeExpression(expression, parameters, arguments);

        public static MethodInvokeExpression InvokeMethod(this Expression expression, string memberName, TypeReference[] parameters, params Expression[] arguments) => new MethodInvokeExpression(new MemberReferenceExpression(expression, memberName), parameters, arguments);

        public static MethodInvokeExpression InvokeMethod(this VariableDeclarationStatement expression, params Expression[] arguments) => InvokeMethod((Expression)expression, arguments);

        public static MethodInvokeExpression InvokeMethod(this VariableDeclarationStatement expression, string memberName, params Expression[] arguments) => InvokeMethod((Expression)expression, memberName, arguments);

        public static MethodInvokeExpression InvokeMethod(this VariableDeclarationStatement expression, TypeReference[] parameters, params Expression[] arguments) => InvokeMethod((Expression)expression, parameters, arguments);

        public static MethodInvokeExpression InvokeMethod(this VariableDeclarationStatement expression, string memberName, TypeReference[] parameters, params Expression[] arguments) => InvokeMethod((Expression)expression, memberName, parameters, arguments);

        public static MemberReferenceExpression GetMember(this Expression expression, string name, params string[] names) 
        {
            var result = new MemberReferenceExpression(expression, name);
            foreach(var n in names)
            {
                result = new MemberReferenceExpression(result, name);
            }

            return result;
        }

        public static BinaryExpression IsNull(this Expression expression) => new BinaryExpression(BinaryOperator.Equals, new TypeReference(typeof(object)).GetMember(nameof(object.ReferenceEquals)).InvokeMethod(expression), new LiteralExpression(true));

        public static BinaryExpression EqualsNull(this Expression expression) => new BinaryExpression(BinaryOperator.Equals, expression, new LiteralExpression(null));

        public static MethodInvokeExpression IsNullOrEmpty(this Expression expression) => new TypeReference(typeof(string)).InvokeMethod(nameof(string.IsNullOrEmpty), expression);

        public static MethodInvokeExpression IsNullOrWhitespace(this Expression expression) => new TypeReference(typeof(string)).InvokeMethod(nameof(string.IsNullOrWhiteSpace), expression);
    }
}
