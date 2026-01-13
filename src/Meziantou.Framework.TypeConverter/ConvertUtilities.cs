#pragma warning disable CS8717 // A member returning a [MaybeNull] value introduces a null value for a type parameter.

namespace Meziantou.Framework;

/// <summary>
/// Provides utility methods for converting values between types with more flexibility than System.Convert.
/// <example>
/// <code><![CDATA[
/// // Using TryChangeType
/// if (ConvertUtilities.TryChangeType("42", out int value))
/// {
///     Console.WriteLine(value); // 42
/// }
///
/// // Using ChangeType with default value
/// var result = ConvertUtilities.ChangeType<int>("invalid", defaultValue: 0);
///
/// // Convert with culture-specific formatting
/// var cultureInfo = CultureInfo.GetCultureInfo("fr-FR");
/// var number = ConvertUtilities.ChangeType<decimal>("1234,56", provider: cultureInfo);
/// ]]></code>
/// </example>
/// </summary>
public static class ConvertUtilities
{
    /// <summary>Gets the default converter instance used by the utility methods.</summary>
    public static IConverter DefaultConverter { get; } = new DefaultConverter();

    /// <summary>Attempts to convert an input value to the specified type using the default converter.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="input">The value to convert.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or the default value if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType<T>(object? input, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out T value)
    {
        return TryChangeType(DefaultConverter, input, provider, out value);
    }

    /// <summary>Attempts to convert an input value to the specified type using a custom converter.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or the default value if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType<T>(this IConverter converter, object? input, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(converter);

        var b = converter.TryChangeType(input, typeof(T), provider, out var v);
        if (!b)
        {
            if (v is null)
            {
                if (typeof(T).IsValueType)
                {
                    value = Activator.CreateInstance<T>()!;
                }
                else
                {
                    value = default!;
                }
            }
            else
            {
                value = (T)v;
            }

            return false;
        }

        value = (T)v!;
        return true;
    }

    /// <summary>Attempts to convert an input value to the specified type using the default converter and invariant culture.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="input">The value to convert.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or the default value if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType<T>(object? input, [MaybeNullWhen(returnValue: false)] out T value)
    {
        return TryChangeType(DefaultConverter, input, out value);
    }

    /// <summary>Attempts to convert an input value to the specified type using a custom converter and invariant culture.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or the default value if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType<T>(this IConverter converter, object? input, [MaybeNullWhen(returnValue: false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(converter);

        return TryChangeType(converter, input, provider: null, out value);
    }

    /// <summary>Attempts to convert an input value to a specified type using the default converter and invariant culture.</summary>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or null if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType(object? input, Type conversionType, out object? value)
    {
        return TryChangeType(DefaultConverter, input, conversionType, provider: null, out value);
    }

    /// <summary>Attempts to convert an input value to a specified type using a custom converter and invariant culture.</summary>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or null if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType(this IConverter converter, object? input, Type conversionType, out object? value)
    {
        ArgumentNullException.ThrowIfNull(converter);

        return TryChangeType(converter, input, conversionType, provider: null, out value);
    }

    /// <summary>Attempts to convert an input value to a specified type using the default converter.</summary>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or null if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType(object? input, Type conversionType, IFormatProvider? provider, out object? value)
    {
        return TryChangeType(DefaultConverter, input, conversionType, provider, out value);
    }

    /// <summary>Attempts to convert an input value to a specified type using a custom converter.</summary>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or null if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryChangeType(this IConverter converter, object? input, Type conversionType, IFormatProvider? provider, out object? value)
    {
        ArgumentNullException.ThrowIfNull(converter);

        return converter.TryChangeType(input, conversionType, provider, out value);
    }

    /// <summary>Converts an input value to a specified type using the default converter, or returns null if conversion fails.</summary>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <see langword="null"/>.</returns>
    public static object? ChangeType(object? input, Type conversionType)
    {
        return ChangeType(DefaultConverter, input, conversionType);
    }

    /// <summary>Converts an input value to a specified type using a custom converter, or returns null if conversion fails.</summary>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <see langword="null"/>.</returns>
    public static object? ChangeType(this IConverter converter, object? input, Type conversionType)
    {
        ArgumentNullException.ThrowIfNull(converter);

        ArgumentNullException.ThrowIfNull(conversionType);

        return ChangeType(converter, input, conversionType, defaultValue: null, provider: null);
    }

    /// <summary>Converts an input value to a specified type using the default converter, or returns a default value if conversion fails.</summary>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static object? ChangeType(object? input, Type conversionType, object? defaultValue)
    {
        return ChangeType(DefaultConverter, input, conversionType, defaultValue, provider: null);
    }

    /// <summary>Converts an input value to a specified type using a custom converter, or returns a default value if conversion fails.</summary>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static object? ChangeType(this IConverter converter, object? input, Type conversionType, object? defaultValue)
    {
        ArgumentNullException.ThrowIfNull(converter);

        ArgumentNullException.ThrowIfNull(conversionType);

        return ChangeType(converter, input, conversionType, defaultValue, provider: null);
    }

    /// <summary>Converts an input value to a specified type using the default converter, or returns a default value if conversion fails.</summary>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static object? ChangeType(object? input, Type conversionType, object? defaultValue, IFormatProvider? provider)
    {
        return ChangeType(DefaultConverter, input, conversionType, defaultValue, provider);
    }

    /// <summary>Converts an input value to a specified type using a custom converter, or returns a default value if conversion fails.</summary>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static object? ChangeType(this IConverter converter, object? input, Type conversionType, object? defaultValue, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(converter);

        ArgumentNullException.ThrowIfNull(conversionType);

        if (defaultValue is null && conversionType.IsValueType)
        {
            defaultValue = Activator.CreateInstance(conversionType);
        }

        if (TryChangeType(converter, input, conversionType, provider, out var value))
            return value;

        return defaultValue;
    }

    /// <summary>Converts an input value to the specified type using the default converter, or returns the default value of the type if conversion fails.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="input">The value to convert.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, the default value of <typeparamref name="T"/>.</returns>
    public static T? ChangeType<T>(object? input)
    {
        return ChangeType<T>(DefaultConverter, input);
    }

    /// <summary>Converts an input value to the specified type using a custom converter, or returns the default value of the type if conversion fails.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, the default value of <typeparamref name="T"/>.</returns>
    public static T? ChangeType<T>(this IConverter converter, object? input)
    {
        ArgumentNullException.ThrowIfNull(converter);

        return ChangeType(converter, input, default(T));
    }

    /// <summary>Converts an input value to the specified type using the default converter, or returns a default value if conversion fails.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="input">The value to convert.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static T? ChangeType<T>(object? input, T defaultValue)
    {
        return ChangeType(DefaultConverter, input, defaultValue);
    }

    /// <summary>Converts an input value to the specified type using a custom converter, or returns a default value if conversion fails.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static T? ChangeType<T>(this IConverter converter, object? input, T defaultValue)
    {
        ArgumentNullException.ThrowIfNull(converter);

        return ChangeType(converter, input, defaultValue, provider: null);
    }

    /// <summary>Converts an input value to the specified type using the default converter, or returns a default value if conversion fails.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="input">The value to convert.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static T? ChangeType<T>(object? input, T defaultValue, IFormatProvider? provider)
    {
        return ChangeType(DefaultConverter, input, defaultValue, provider);
    }

    /// <summary>Converts an input value to the specified type using a custom converter, or returns a default value if conversion fails.</summary>
    /// <typeparam name="T">The target type to convert to.</typeparam>
    /// <param name="converter">The converter to use for the conversion.</param>
    /// <param name="input">The value to convert.</param>
    /// <param name="defaultValue">The value to return if conversion fails.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <returns>The converted value if conversion succeeded; otherwise, <paramref name="defaultValue"/>.</returns>
    public static T? ChangeType<T>(this IConverter converter, object? input, T defaultValue, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(converter);

        if (TryChangeType(converter, input, provider, out T? value))
            return value;

        return defaultValue;
    }
}
