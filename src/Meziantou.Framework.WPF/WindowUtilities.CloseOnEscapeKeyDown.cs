using System.Windows;
using System.Windows.Input;

namespace Meziantou.Framework.WPF
{
    public static partial class WindowUtilities
    {
        /// <summary>
        /// Usage <code>&lt;Window xmlns:utilities=&quot;clr-namespace:Meziantou.Framework.WPF;assembly=Meziantou.Framework.WPF&quot; utilities:WindowUtilities.CloseOnEscapeKeyDown=&quot;True&quot;&gt;</code>
        /// </summary>
        public static readonly DependencyProperty CloseOnEscapeProperty = DependencyProperty.RegisterAttached(
           "CloseOnEscapeKeyDown",
           typeof(bool),
           typeof(WindowUtilities),
           new FrameworkPropertyMetadata(defaultValue: false, CloseOnEscapeKeyDownChanged));

        public static bool GetCloseOnEscapeKeyDown(DependencyObject d)
        {
            return (bool)d.GetValue(CloseOnEscapeProperty);
        }

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
}
