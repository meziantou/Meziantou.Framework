using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.Yamlish;

internal static class ExpressionExtensions
{
    public static IReadOnlyCollection<MemberInfo> GetMemberInfos<T>(this Expression<Func<T, object>> member)
    {
        var body = UnwrapConversion(member.Body);
        if (body is MemberExpression { Member: PropertyInfo or FieldInfo } memberExpression)
            return [memberExpression.Member];

        if (body is NewExpression newExpression)
        {
            var members = new List<MemberInfo>();
            foreach (var argument in newExpression.Arguments)
            {
                if (argument is not MemberExpression { Member: PropertyInfo or FieldInfo } argumentMemberExpression)
                    return [];

                members.Add(argumentMemberExpression.Member);
            }

            return members;
        }

        return [];
    }

    private static Expression UnwrapConversion(Expression expression)
    {
        return expression is UnaryExpression { NodeType: ExpressionType.Convert } unaryExpression ? unaryExpression.Operand : expression;
    }
}
