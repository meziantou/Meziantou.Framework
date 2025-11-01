namespace Meziantou.Framework;

/// <summary>Defines methods for converting values between types.</summary>
public interface IConverter
{
    /// <summary>Attempts to convert an input value to a specified type.</summary>
    /// <param name="input">The value to convert.</param>
    /// <param name="conversionType">The type to convert to.</param>
    /// <param name="provider">An object that provides culture-specific formatting information.</param>
    /// <param name="value">When this method returns, contains the converted value if the conversion succeeded, or null if the conversion failed.</param>
    /// <returns><see langword="true"/> if the conversion succeeded; otherwise, <see langword="false"/>.</returns>
    bool TryChangeType(object? input, Type conversionType, IFormatProvider? provider, out object? value);
}
