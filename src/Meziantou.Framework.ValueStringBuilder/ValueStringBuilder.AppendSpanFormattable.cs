namespace Meziantou.Framework;

// https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/ValueStringBuilder.AppendSpanFormattable.cs
#if PUBLIC_VALUESTRINGBUILDER
public
#else
internal
#endif
ref partial struct ValueStringBuilder
{
    /// <summary>
    /// Appends <paramref name="value" /> by formatting it straight into the buffer, falling back to
    /// <see cref="IFormattable.ToString(string, IFormatProvider)" /> when it does not fit.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <param name="format">The format to use, or <see langword="null" /> for the default format.</param>
    /// <param name="provider">The provider to use, or <see langword="null" /> for the current culture.</param>
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
}
