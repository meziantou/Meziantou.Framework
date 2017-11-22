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
            var condition = new ConditionStatement
            {
                Condition = new BinaryExpression(BinaryOperator.Equals, argument, new LiteralExpression(null)),
                TrueStatements = new ThrowStatement(new NewObjectExpression(typeof(ArgumentNullException), new NameofExpression(argument)))
            };
            return condition;
        }
    }
}
