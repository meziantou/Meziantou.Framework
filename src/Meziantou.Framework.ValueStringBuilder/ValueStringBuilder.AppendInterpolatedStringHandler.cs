using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

#if PUBLIC_VALUESTRINGBUILDER
public
#else
internal
#endif
ref partial struct ValueStringBuilder
{
    public void Append([InterpolatedStringHandlerArgument("")] ref AppendInterpolatedStringHandler handler)
    {
        AppendAndDispose(ref handler);
    }

    public void Append(IFormatProvider? provider, [InterpolatedStringHandlerArgument("", nameof(provider))] ref AppendInterpolatedStringHandler handler)
    {
        _ = provider;

        AppendAndDispose(ref handler);
    }

    private void AppendAndDispose(ref AppendInterpolatedStringHandler handler)
    {
        try
        {
            Append(handler._valueStringBuilder.AsSpan());
        }
        finally
        {
            handler._valueStringBuilder.Dispose();
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [InterpolatedStringHandler]
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The handler is only created by the compiler for an interpolated string argument, and the Append overload it is passed to always disposes the buffer.")]
    public ref struct AppendInterpolatedStringHandler
    {
        // Matches the heuristic used by DefaultInterpolatedStringHandler: assume ~11 characters per hole.
        private const int GuessedLengthPerHole = 11;
        private const int MinimumLength = 16;

        internal ValueStringBuilder _valueStringBuilder;
        private readonly IFormatProvider? _provider;
        private readonly bool _hasCustomFormatter;

        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder valueStringBuilder)
        {
            // The handler must not share the buffer of the builder it appends to. It receives a copy of that
            // builder, so growing it here would return the caller's pooled array while the caller still points
            // at it, and an exception thrown between two holes would leave the copy - and the appended text -
            // unreachable. Building into a private buffer keeps the caller's builder untouched until Append runs.
            _ = valueStringBuilder;
            _valueStringBuilder = new ValueStringBuilder(GetInitialCapacity(literalLength, formattedCount));
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
            _ = valueStringBuilder;
            _valueStringBuilder = new ValueStringBuilder(GetInitialCapacity(literalLength, formattedCount));
            _provider = provider;
            _hasCustomFormatter = provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter;
        }

        public AppendInterpolatedStringHandler(int literalLength, int formattedCount, ValueStringBuilder valueStringBuilder, IFormatProvider? provider, out bool shouldAppend)
            : this(literalLength, formattedCount, valueStringBuilder, provider)
        {
            shouldAppend = true;
        }

        public void AppendLiteral(string value) => _valueStringBuilder.Append(value);

        public void AppendFormatted<T>(T value) => AppendFormatted(value, format: null);

        public void AppendFormatted<T>(T value, string? format)
        {
            if (_hasCustomFormatter)
            {
                AppendCustomFormatter(value, format);
                return;
            }

            if (value is IFormattable)
            {
                // ISpanFormattable formats straight into the buffer. IFormattable.ToString allocates a
                // string for every hole, so it is only the fallback for types that cannot do better.
                if (value is ISpanFormattable)
                {
                    int charsWritten;
                    while (!((ISpanFormattable)value).TryFormat(_valueStringBuilder._chars[_valueStringBuilder._pos..], out charsWritten, format, _provider))
                    {
                        _valueStringBuilder.EnsureCapacity(_valueStringBuilder.Capacity + 1);
                    }

                    _valueStringBuilder._pos += charsWritten;
                    return;
                }

                _valueStringBuilder.Append(((IFormattable)value).ToString(format, _provider));
            }
            else
            {
                _valueStringBuilder.Append(value?.ToString());
            }
        }

        public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, format: null);

        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            var startingPos = _valueStringBuilder.Length;
            AppendFormatted(value, format);

            if (alignment is not 0)
            {
                // Formatting first and padding afterwards avoids materialising the value as a string
                // just to measure it.
                AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
            }
        }

        private void AppendOrInsertAlignmentIfNeeded(int startingPos, int alignment)
        {
            var charsWritten = _valueStringBuilder.Length - startingPos;

            var leftAlign = false;
            if (alignment < 0)
            {
                leftAlign = true;
                alignment = -alignment;
            }

            var paddingRequired = alignment - charsWritten;
            if (paddingRequired <= 0)
            {
                return;
            }

            if (leftAlign)
            {
                _valueStringBuilder.Append(' ', paddingRequired);
            }
            else
            {
                _valueStringBuilder.Insert(startingPos, ' ', paddingRequired);
            }
        }

        public void AppendFormatted(ReadOnlySpan<char> value) => _valueStringBuilder.Append(value);

        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
        {
            _ = format;

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

        private static int GetInitialCapacity(int literalLength, int formattedCount)
        {
            return Math.Max(MinimumLength, literalLength + (formattedCount * GuessedLengthPerHole));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AppendCustomFormatter<T>(T value, string? format)
        {
            if (_provider?.GetFormat(typeof(ICustomFormatter)) is ICustomFormatter formatter)
            {
                _valueStringBuilder.Append(formatter.Format(format, value, _provider));
            }
        }
    }
}
