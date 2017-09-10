using System;
using System.Reflection;

namespace Meziantou.Framework.Utilities
{
    public static class ConvertUtilities
    {
        public static IConverter DefaultConverter { get; } = new DefaultConverter();

        public static bool TryChangeType<T>(object input, IFormatProvider provider, out T value)
        {
            return TryChangeType(DefaultConverter, input, provider, out value);
        }

        public static bool TryChangeType<T>(this IConverter converter, object input, IFormatProvider provider, out T value)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            var b = converter.TryChangeType(input, typeof(T), provider, out object v);
            if (!b)
            {
                if (v == null)
                {
                    if (typeof(T).GetTypeInfo().IsValueType)
                    {
                        value = (T)Activator.CreateInstance(typeof(T));
                    }
                    else
                    {
                        value = default(T);
                    }
                }
                else
                {
                    value = (T)v;
                }

                return false;
            }

            value = (T)v;
            return true;
        }

        public static bool TryChangeType<T>(object input, out T value)
        {
            return TryChangeType(DefaultConverter, input, out value);
        }

        public static bool TryChangeType<T>(this IConverter converter, object input, out T value)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return TryChangeType(converter, input, null, out value);
        }

        public static bool TryChangeType(object input, Type conversionType, out object value)
        {
            return TryChangeType(DefaultConverter, input, conversionType, out value);
        }

        public static bool TryChangeType(this IConverter converter, object input, Type conversionType, out object value)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return TryChangeType(converter, input, conversionType, null, out value);
        }

        public static bool TryChangeType(object input, Type conversionType, IFormatProvider provider, out object value)
        {
            return TryChangeType(DefaultConverter, input, conversionType, provider, out value);
        }

        public static bool TryChangeType(this IConverter converter, object input, Type conversionType, IFormatProvider provider, out object value)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            return converter.TryChangeType(input, conversionType, provider, out value);
        }

        public static object ChangeType(object input, Type conversionType)
        {
            return ChangeType(DefaultConverter, input, conversionType);
        }

        public static object ChangeType(this IConverter converter, object input, Type conversionType)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ChangeType(converter, input, conversionType, null, null);
        }

        public static object ChangeType(object input, Type conversionType, object defaultValue)
        {
            return ChangeType(DefaultConverter, input, conversionType, defaultValue);
        }

        public static object ChangeType(this IConverter converter, object input, Type conversionType, object defaultValue)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ChangeType(converter, input, conversionType, defaultValue, null);
        }

        public static object ChangeType(object input, Type conversionType, object defaultValue, IFormatProvider provider)
        {
            return ChangeType(DefaultConverter, input, conversionType, defaultValue, provider);
        }

        public static object ChangeType(this IConverter converter, object input, Type conversionType, object defaultValue, IFormatProvider provider)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            if (conversionType == null)
                throw new ArgumentNullException(nameof(conversionType));

            if (defaultValue == null && conversionType.GetTypeInfo().IsValueType)
            {
                defaultValue = Activator.CreateInstance(conversionType);
            }

            if (TryChangeType(converter, input, conversionType, provider, out object value))
                return value;

            return defaultValue;
        }

        public static T ChangeType<T>(object input)
        {
            return ChangeType<T>(DefaultConverter, input);
        }

        public static T ChangeType<T>(this IConverter converter, object input)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ChangeType(converter, input, default(T));
        }

        public static T ChangeType<T>(object input, T defaultValue)
        {
            return ChangeType<T>(DefaultConverter, input, defaultValue);
        }

        public static T ChangeType<T>(this IConverter converter, object input, T defaultValue)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            return ChangeType(converter, input, defaultValue, null);
        }

        public static T ChangeType<T>(object input, T defaultValue, IFormatProvider provider)
        {
            return ChangeType<T>(DefaultConverter, input, defaultValue, provider);
        }

        public static T ChangeType<T>(this IConverter converter, object input, T defaultValue, IFormatProvider provider)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            if (TryChangeType(converter, input, provider, out T value))
                return value;

            return defaultValue;
        }
    }
}
