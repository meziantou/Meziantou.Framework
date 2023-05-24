﻿using System.Linq.Expressions;

namespace Meziantou.Framework;

internal readonly struct CoercedAwaitableInfo
{
    public AwaitableInfo AwaitableInfo { get; }
    public Expression? CoercerExpression { get; }
    public Type? CoercerResultType { get; }
    public bool RequiresCoercion => CoercerExpression != null;

    public CoercedAwaitableInfo(AwaitableInfo awaitableInfo)
    {
        AwaitableInfo = awaitableInfo;
        CoercerExpression = null;
        CoercerResultType = null;
    }

    public CoercedAwaitableInfo(Expression coercerExpression, Type coercerResultType, AwaitableInfo coercedAwaitableInfo)
    {
        CoercerExpression = coercerExpression;
        CoercerResultType = coercerResultType;
        AwaitableInfo = coercedAwaitableInfo;
    }

    public static bool IsTypeAwaitable(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)] Type type,
        out CoercedAwaitableInfo info)
    {
        if (AwaitableInfo.IsTypeAwaitable(type, out var directlyAwaitableInfo))
        {
            info = new CoercedAwaitableInfo(directlyAwaitableInfo);
            return true;
        }

        // It's not directly awaitable, but maybe we can coerce it.
        // Currently we support coercing FSharpAsync<T>.
        if (ObjectMethodExecutorFSharpSupport.TryBuildCoercerFromFSharpAsyncToAwaitable(type,
            out var coercerExpression,
            out var coercerResultType))
        {
            if (AwaitableInfo.IsTypeAwaitable(coercerResultType, out var coercedAwaitableInfo))
            {
                info = new CoercedAwaitableInfo(coercerExpression, coercerResultType, coercedAwaitableInfo);
                return true;
            }
        }

        info = default;
        return false;
    }
}