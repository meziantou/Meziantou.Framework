using System;

namespace Meziantou.Framework.Toml.Helpers;

internal static class ThrowHelper
{
    public static InvalidOperationException GetExpectingNoParentException()
    {
        return new InvalidOperationException("The node is already attached to another parent");
    }
}
