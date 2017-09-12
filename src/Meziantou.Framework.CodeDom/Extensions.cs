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

        public static CodeNamespaceDeclaration AddNamespace(this INamespaceDeclarationContainer unit, string name)
        {
            var ns = new CodeNamespaceDeclaration(name);
            unit.Namespaces.Add(ns);
            return ns;
        }

        public static CodeNamespaceDeclaration AddNamespace(this INamespaceDeclarationContainer unit, CodeNamespaceDeclaration ns)
        {
            unit.Namespaces.Add(ns);
            return ns;
        }

        public static T AddType<T>(this ITypeDeclarationContainer unit, T type) where T : CodeTypeDeclaration
        {
            unit.Types.Add(type);
            return type;
        }

        public static CodeUsingDirective AddUsing(this IUsingDirectiveContainer unit, string ns)
        {
            var directive = new CodeUsingDirective(ns);
            unit.Usings.Add(directive);
            return directive;
        }

        public static CodeMethodArgumentDeclaration AddArgument(this CodeMethodDeclaration method, CodeMethodArgumentDeclaration argument)
        {
            method.Arguments.Add(argument);
            return argument;
        }

        public static CodeMethodArgumentDeclaration AddArgument(this CodeMethodDeclaration method, string name, CodeTypeReference type)
        {
            var argument = new CodeMethodArgumentDeclaration(type, name);
            method.Arguments.Add(argument);
            return argument;
        }

        public static T AddMember<T>(this CodeClassDeclaration c, T member) where T : CodeMemberDeclaration
        {
            c.Members.Add(member);
            return member;
        }

        public static CodeConditionStatement CreateThrowIfNullStatement(this CodeMethodArgumentDeclaration argument)
        {
            CodeConditionStatement condition = new CodeConditionStatement();
            condition.Condition = new CodeBinaryExpression(BinaryOperator.Equals, argument, new CodeLiteralExpression(null));
            condition.TrueStatements = new CodeThrowStatement(new CodeNewObjectExpression(typeof(ArgumentNullException), new CodeNameofExpression(argument)));
            return condition;
        }
    }
}
