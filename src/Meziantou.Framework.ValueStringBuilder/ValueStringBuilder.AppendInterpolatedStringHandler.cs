using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

public ref partial struct ValueStringBuilder
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [InterpolatedStringHandler]
    public ref struct AppendInterpolatedStringHandler
    {
        internal ValueStringBuilder _valueStringBuilder;
        private readonly IFormatProvider? _provider;
        private readonly bool _hasCustomFormatter;

        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder valueStringBuilder)
        {
            _valueStringBuilder = valueStringBuilder;
            _provider = null;
            _hasCustomFormatter = false;
        }

        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder valueStringBuilder, out bool shouldAppend)
            : this(literalLength, formattedCount, valueStringBuilder)
        {
            shouldAppend = true;
        }

        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder valueStringBuilder, IFormatProvider? provider)
        {
            _valueStringBuilder = valueStringBuilder;
            _provider = provider;
            _hasCustomFormatter = provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter;
        }

        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder valueStringBuilder, IFormatProvider? provider, out bool shouldAppend)
            : this(literalLength, formattedCount, valueStringBuilder, provider)
        {
            shouldAppend = true;
        }

        public void AppendLiteral(string value) => _valueStringBuilder.Append(value);

        public void AppendFormatted<T>(T value)
        {
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format: null);
                return;
            }

            if (value is null)
            {
                return;
            }

            if (value is IFormattable formattable)
            {
                _valueStringBuilder.Append(formattable.ToString(format: null, _provider));
            }
            else
            {
                _valueStringBuilder.Append(value.ToString());
            }
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format);
                return;
            }

            if (value is null)
            {
                return;
            }

            if (value is IFormattable formattable)
            {
                _valueStringBuilder.Append(formattable.ToString(format, _provider));
            }
            else
            {
                _valueStringBuilder.Append(value.ToString());
            }
        }

        public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, format: null);

        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            if (alignment is 0)
            {
                AppendFormatted(value, format);
                return;
            }

            var formatted = FormatToString(value, format);
            AppendFormatted(formatted.AsSpan(), alignment);
        }

        public void AppendFormatted(ReadOnlySpan<char> value) => _valueStringBuilder.Append(value);

        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
        {
            if (alignment is 0)
            {
                _valueStringBuilder.Append(value);
                return;
            }

            var leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            var paddingRequired = alignment - value.Length;
            if (paddingRequired <= 0)
            {
                _valueStringBuilder.Append(value);
            }
            else if (leftAlign)
            {
                _valueStringBuilder.Append(value);
                _valueStringBuilder.Append(' ', paddingRequired);
            }
            else
            {
                _valueStringBuilder.Append(' ', paddingRequired);
                _valueStringBuilder.Append(value);
            }
        }

        public void AppendFormatted(string? value)
        {
            if (!_hasCustomFormatter)
            {
                _valueStringBuilder.Append(value);
            }
            else
            {
                AppendFormatted<string?>(value);
            }
        }

        public void AppendFormatted(string? value, int alignment = 0, string? format = null) => AppendFormatted<string?>(value, alignment, format);

        public void AppendFormatted(object? value, int alignment = 0, string? format = null) => AppendFormatted<object?>(value, alignment, format);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendCustomFormatter<T>(T value, string? format)
        {
            if (_provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter formatter)
            {
                _valueStringBuilder.Append(formatter.Format(format, value, _provider));
            }
        }

        private string FormatToString<T>(T value, string? format)
        {
            if (_hasCustomFormatter && _provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter formatter)
            {
                return formatter.Format(format, value, _provider) ?? string.Empty;
            }

            if (value is null)
            {
                return string.Empty;
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(format, _provider) ?? string.Empty;
            }

            return value.ToString() ?? string.Empty;
        }
    }
}
