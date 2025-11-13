using System.Windows.Data;

namespace Meziantou.Framework.WPF;

/// <summary>Converts boolean values to custom values for WPF data binding.</summary>
/// <example>
/// <code>
/// &lt;Window.Resources&gt;
///     &lt;wpf:BooleanToValueConverter x:Key="BoolToVisibility" TrueValue="{x:Static Visibility.Visible}" FalseValue="{x:Static Visibility.Collapsed}" /&gt;
/// &lt;/Window.Resources&gt;
/// &lt;TextBlock Visibility="{Binding IsEnabled, Converter={StaticResource BoolToVisibility}}" /&gt;
/// </code>
/// </example>
public sealed class BooleanToValueConverter : IValueConverter
{
    /// <summary>Gets or sets the value to return when the input is <see langword="true"/>.</summary>
    public object? TrueValue { get; set; }

    /// <summary>Gets or sets the value to return when the input is <see langword="false"/>.</summary>
    public object? FalseValue { get; set; }

    /// <summary>Gets or sets the value to return when the input is <see langword="null"/>. If not set, <see cref="FalseValue"/> is used instead.</summary>
    public object? NullValue { get; set; }

    private object? GetValue(bool value)
    {
        return value ? TrueValue : FalseValue;
    }

    /// <summary>Converts a value to the corresponding <see cref="TrueValue"/> or <see cref="FalseValue"/>.</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is null)
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

    /// <summary>This method is not supported and always throws <see cref="NotSupportedException"/>.</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotSupportedException();
    }
}
