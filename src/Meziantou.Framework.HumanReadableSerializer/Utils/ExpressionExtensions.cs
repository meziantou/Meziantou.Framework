using System.Linq.Expressions;
using System.Reflection;

namespace Meziantou.Framework.HumanReadable;

internal static class ExpressionExtensions
{
    // Returns the members and the static type of the instance they are accessed on (e.g. T for x => x.Name, and Address for x => x.Address.City)
    public static IReadOnlyCollection<(Type OwnerType, MemberInfo Member)> GetMemberInfos<T>(this Expression<Func<T, object>> member)
    {
        var body = member.Body.UnwrapConversion();
        if (GetMember(body) is { } result)
            return [result];

        if (body is NewExpression newExpression)
        {
            var members = new List<(Type OwnerType, MemberInfo Member)>();
            foreach (var argument in newExpression.Arguments)
            {
                if (GetMember(argument) is not { } argumentMember)
                    return []; // Not supported expression

                members.Add(argumentMember);
            }

            return members;
        }

        return [];

        static (Type OwnerType, MemberInfo Member)? GetMember(Expression expression)
        {
            if (expression is MemberExpression { Member: PropertyInfo or FieldInfo } memberExpression)
            {
                var ownerType = memberExpression.Expression?.Type ?? memberExpression.Member.DeclaringType;
                if (ownerType is not null)
                    return (ownerType, memberExpression.Member);
            }

            return null;
        }
    }

    public static Expression UnwrapConversion(this Expression expression)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unaryExpression)
            return unaryExpression.Operand;

        return expression;
    }
}
