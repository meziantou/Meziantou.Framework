using System;
using System.Globalization;
using System.Windows.Data;

namespace Meziantou.Framework.WPF
{
    public sealed class BooleanToValueConverter : IValueConverter
    {
        public object? TrueValue { get; set; }
        public object? FalseValue { get; set; }

        /// <summary>
        /// If not set, the converter use the <see cref="FalseValue"/> instead
        /// </summary>
        public object? NullValue { get; set; }

        private object? GetValue(bool value)
        {
            return value ? TrueValue : FalseValue;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
        {
            if (value == null)
                return NullValue ?? FalseValue;

            if (value is bool b)
                return GetValue(b);

            if (value is IConvertible convertible)
            {
                try
                {
                    return GetValue(convertible.ToBoolean(culture));
                }
                catch (FormatException)
                {
                }
                catch (InvalidCastException)
                {
                }
            }

            return NullValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
        {
            throw new NotSupportedException();
        }
    }
}
