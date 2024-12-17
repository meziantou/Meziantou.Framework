using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable;

internal static class ExpressionExtensions
{
    public static IReadOnlyCollection<MemberInfo> GetMemberInfos<T>(this Expression<Func<T, object>> member)
    {
        var memberInfo = member.GetMemberInfo();
        if (memberInfo is not null)
        {
            if (memberInfo is PropertyInfo propertyInfo)
            {
                return [propertyInfo];
            }

            if (memberInfo is FieldInfo fieldInfo)
            {
                return [fieldInfo];
            }
        }

        if (member.Body.UnwrapConversion() is NewExpression newExpression)
        {
            var types = new List<MemberInfo>();
            foreach (var argument in newExpression.Arguments)
            {
                if (argument is MemberExpression argumentMemberExpression)
                {
                    if (argumentMemberExpression.Member is PropertyInfo propertyInfo)
                    {
                        types.Add(propertyInfo);
                        continue;
                    }

                    if (argumentMemberExpression.Member is FieldInfo fieldInfo)
                    {
                        types.Add(fieldInfo);
                        continue;
                    }
                }

                // Not supported expression
                return [];
            }

            return types;
        }

        return [];
    }

    public static MemberInfo? GetMemberInfo<T>(this Expression<Func<T, object>> member)
    {
        var body = UnwrapConversion(member.Body);

        if (body is MemberExpression memberExpression)
        {
            return memberExpression.Member;
        }

        return null;
    }

    public static Expression UnwrapConversion(this Expression expression)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unaryExpression)
            return unaryExpression.Operand;

        return expression;
    }
}
