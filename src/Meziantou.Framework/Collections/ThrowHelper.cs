namespace Meziantou.Framework.Collections;

internal static class ThrowHelper
{
    [DoesNotReturn]
    [SuppressMessage("Usage", "MA0015:Specify the parameter name in ArgumentException", Justification = "The parameter name is a constant")]
    internal static void ThrowArgumentOutOfRange_IndexException()
    {
        throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
    }

    [DoesNotReturn]
    internal static void ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion()
    {
        throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
    }

    [DoesNotReturn]
    internal static void ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen()
    {
        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
    }
}
