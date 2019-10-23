using System;

namespace Meziantou.Framework.CodeDom
{
    public static class Extensions
    {
        public static T? GetSelfOrParentOfType<T>(this CodeObject? codeObject) where T : CodeObject
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

        public static MethodInvokeExpression CreateInvokeMethodExpression(this Expression expression, params Expression[] arguments) => new MethodInvokeExpression(expression, arguments);

        public static MethodInvokeExpression CreateInvokeMethodExpression(this Expression expression, string memberName, params Expression[] arguments) => new MethodInvokeExpression(new MemberReferenceExpression(expression, memberName), arguments);

        public static MethodInvokeExpression CreateInvokeMethodExpression(this Expression expression, TypeReference[] parameters, params Expression[] arguments) => new MethodInvokeExpression(expression, parameters, arguments);

        public static MethodInvokeExpression CreateInvokeMethodExpression(this Expression expression, string memberName, TypeReference[] parameters, params Expression[] arguments) => new MethodInvokeExpression(new MemberReferenceExpression(expression, memberName), parameters, arguments);

        public static MethodInvokeExpression CreateInvokeMethodExpression(this VariableDeclarationStatement expression, params Expression[] arguments) => CreateInvokeMethodExpression((Expression)expression, arguments);

        public static MethodInvokeExpression CreateInvokeMethodExpression(this VariableDeclarationStatement expression, string memberName, params Expression[] arguments) => CreateInvokeMethodExpression((Expression)expression, memberName, arguments);

        public static MethodInvokeExpression CreateInvokeMethodExpression(this VariableDeclarationStatement expression, TypeReference[] parameters, params Expression[] arguments) => CreateInvokeMethodExpression((Expression)expression, parameters, arguments);

        public static MethodInvokeExpression CreateInvokeMethodExpression(this VariableDeclarationStatement expression, string memberName, TypeReference[] parameters, params Expression[] arguments) => CreateInvokeMethodExpression((Expression)expression, memberName, parameters, arguments);

        public static MemberReferenceExpression CreateMemberReferenceExpression(this Expression expression, string name, params string[] names)
        {
            var result = new MemberReferenceExpression(expression, name);
            foreach (var n in names)
            {
                result = new MemberReferenceExpression(result, n);
            }

            return result;
        }

        public static MemberReferenceExpression CreateMemberReferenceExpression(this VariableDeclarationStatement variable, string name, params string[] names) => CreateMemberReferenceExpression(new VariableReferenceExpression(variable), name, names);

        public static MemberReferenceExpression CreateMemberReferenceExpression(this MethodArgumentDeclaration argument, string name, params string[] names) => CreateMemberReferenceExpression(new ArgumentReferenceExpression(argument), name, names);

        public static BinaryExpression CreateIsNullExpression(this Expression expression) => new BinaryExpression(BinaryOperator.Equals, new TypeReference(typeof(object)).CreateMemberReferenceExpression(nameof(object.ReferenceEquals)).CreateInvokeMethodExpression(expression), new LiteralExpression(value: true));

        public static BinaryExpression CreateEqualsNullExpression(this Expression expression) => new BinaryExpression(BinaryOperator.Equals, expression, new LiteralExpression(value: null));

        public static MethodInvokeExpression CreateIsNullOrEmptyExpression(this Expression expression) => new TypeReference(typeof(string)).CreateInvokeMethodExpression(nameof(string.IsNullOrEmpty), expression);

        public static MethodInvokeExpression CreateIsNullOrWhitespaceExpression(this Expression expression) => new TypeReference(typeof(string)).CreateInvokeMethodExpression(nameof(string.IsNullOrWhiteSpace), expression);
    }
}
