namespace Meziantou.Framework;

// https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/ValueStringBuilder.AppendSpanFormattable.cs
#if PUBLIC_VALUESTRINGBUILDER
public
#else
internal
#endif
ref partial struct ValueStringBuilder
{
#if NET6_0_OR_GREATER
    public void AppendSpanFormattable<T>(T value, string? format = null, IFormatProvider? provider = null) where T : ISpanFormattable
    {
        if (value.TryFormat(_chars.Slice(_pos), out var charsWritten, format, provider))
        {
            _pos += charsWritten;
        }
        else
        {
            Append(value.ToString(format, provider));
        }
    }
#endif
}
