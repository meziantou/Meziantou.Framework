using System.Windows;
using System.Windows.Input;

namespace Meziantou.Framework.WPF;

/// <summary>
/// Provides utility methods and attached properties for WPF windows.
/// </summary>
/// <example>
/// <code>
/// &lt;Window xmlns:wpf="clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF"
///         wpf:WindowUtilities.CloseOnEscapeKeyDown="True"&gt;
/// &lt;/Window&gt;
/// </code>
/// </example>
public static partial class WindowUtilities
{
    /// <summary>Identifies the CloseOnEscapeKeyDown attached property.</summary>
    public static readonly DependencyProperty CloseOnEscapeProperty = DependencyProperty.RegisterAttached(
       "CloseOnEscapeKeyDown",
       typeof(bool),
       typeof(WindowUtilities),
       new FrameworkPropertyMetadata(defaultValue: false, CloseOnEscapeKeyDownChanged));

    /// <summary>Gets the value of the CloseOnEscapeKeyDown attached property for a specified dependency object.</summary>
    /// <param name="d">The dependency object.</param>
    /// <returns><see langword="true"/> if the window closes on Escape key down; otherwise, <see langword="false"/>.</returns>
    public static bool GetCloseOnEscapeKeyDown(DependencyObject d)
    {
        return (bool)d.GetValue(CloseOnEscapeProperty);
    }

    /// <summary>Sets the value of the CloseOnEscapeKeyDown attached property for a specified dependency object.</summary>
    /// <param name="d">The dependency object.</param>
    /// <param name="value"><see langword="true"/> to close the window on Escape key down; otherwise, <see langword="false"/>.</param>
    public static void SetCloseOnEscapeKeyDown(DependencyObject d, bool value)
    {
        d.SetValue(CloseOnEscapeProperty, value);
    }

    private static void CloseOnEscapeKeyDownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window target)
        {
            if ((bool)e.NewValue)
            {
                target.PreviewKeyDown += CloseOnEscapeKeyDown_PreviewKeyDown;
            }
            else
            {
                target.PreviewKeyDown -= CloseOnEscapeKeyDown_PreviewKeyDown;
            }
        }
    }

    private static void CloseOnEscapeKeyDown_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is Window target)
        {
            if (e.Key == Key.Escape)
            {
                target.Close();
            }
        }
    }
}
