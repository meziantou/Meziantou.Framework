// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// Inspired by CoreLib, taken from https://github.com/MihaZupan/SharpCollections, cc @MihaZupan
/// </summary>
[ExcludeFromCodeCoverage]
[SuppressMessage("Usage", "MA0015:Specify the parameter name in ArgumentException", Justification = "Throw helpers: the parameter names belong to the callers")]
internal static class ThrowHelper
{
    // Very conservative limit used to limit nesting in the final AST.
    // Used to avoid a StackOverflow in the recursive rendering process.
    internal const int DefaultDepthLimit = 128;

    // Limit used for reducing the maximum execution time for pathological-case inputs.
    // Applies to:
    // a) inputs that would fail depth checks in the future (for example "[[[[[..." or ">>>>>>...")
    // b) very large pipe tables.
    internal const int LargeDepthLimit = 10 * 1024;

    [DoesNotReturn]
    public static void ArgumentException(string message) => throw new ArgumentException(message);

    [DoesNotReturn]
    public static void ArgumentException(string message, string paramName) => throw new ArgumentException(message, paramName);

    [DoesNotReturn]
    public static void ArgumentOutOfRangeException(string paramName) => throw new ArgumentOutOfRangeException(paramName);

    [DoesNotReturn]
    public static void ArgumentOutOfRangeException(string message, string paramName) => throw new ArgumentOutOfRangeException(paramName, message);

    [DoesNotReturn]
    public static void ArgumentOutOfRangeException_index() => throw new ArgumentOutOfRangeException("index");

    [DoesNotReturn]
    public static void InvalidOperationException(string message) => throw new InvalidOperationException(message);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CheckDepthLimit(int depth, bool useLargeLimit = false)
    {
        int limit = useLargeLimit ? LargeDepthLimit : DefaultDepthLimit;

        CheckDepthLimit(depth, limit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CheckDepthLimit(int depth, int limit)
    {
        if (depth > limit)
            DepthLimitExceeded();
    }

    // Backstop for recursive code paths when the configured depth limit is too high for the current thread's stack
    public static void CheckSufficientExecutionStack()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            DepthLimitExceeded();
    }

    [DoesNotReturn]
    private static void DepthLimitExceeded() => throw new ArgumentException("Markdown elements in the input are too deeply nested - depth limit exceeded. Input is most likely not sensible or is a very large table.");

    [DoesNotReturn]
    public static void ThrowArgumentException(ExceptionArgument argument, ExceptionReason reason)
    {
        throw new ArgumentException(argument.ToString(), GetExceptionReason(reason));
    }

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRangeException(ExceptionArgument argument, ExceptionReason reason)
    {
        throw new ArgumentOutOfRangeException(argument.ToString(), GetExceptionReason(reason));
    }

    [DoesNotReturn]
    [SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Indexers behave like the string indexer")]
    [SuppressMessage("Design", "MA0012:Do not raise reserved exception type", Justification = "Indexers behave like the string indexer")]
    public static void ThrowIndexOutOfRangeException()
    {
        throw new IndexOutOfRangeException();
    }

    private static string GetExceptionReason(ExceptionReason reason)
    {
        switch (reason)
        {
            case ExceptionReason.String_Empty:
                return "String must not be empty.";

            case ExceptionReason.SmallCapacity:
                return "Capacity was less than the current size.";

            case ExceptionReason.InvalidOffsetLength:
                return "Offset and length must refer to a position in the string.";

            case ExceptionReason.DuplicateKey:
                return "The given key is already present in the dictionary.";

            default:
                Debug.Assert(false, "The enum value is not defined, please check the ExceptionReason Enum.");
                return "";
        }
    }
}
