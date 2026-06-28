using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class NumericHelpers
{
    public static bool IsZero(Optional<object?> value)
    {
        if (!value.HasValue || value.Value is null)
            return false;

        return value.Value switch
        {
            byte v => v == 0,
            sbyte v => v == 0,
            short v => v == 0,
            ushort v => v == 0,
            int v => v == 0,
            uint v => v == 0,
            long v => v == 0,
            ulong v => v == 0,
            float v => v == 0,
            double v => v == 0,
            decimal v => v == 0,
            _ => false,
        };
    }
}
