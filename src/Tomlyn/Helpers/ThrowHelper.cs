// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;

namespace Tomlyn.Helpers
{
    internal static class ThrowHelper
    {
        public static ArgumentOutOfRangeException GetIndexNegativeArgumentOutOfRangeException(string paramName)
        {
            return new ArgumentOutOfRangeException(paramName, "Index must be positive");
        }
        public static ArgumentOutOfRangeException GetIndexArgumentOutOfRangeException(string paramName, int maxValue)
        {
            return new ArgumentOutOfRangeException(paramName, $"Index must be less than {maxValue}");
        }
        public static InvalidOperationException GetExpectingNoParentException()
        {
            return new InvalidOperationException("The node is already attached to another parent");
        }
    }
}
