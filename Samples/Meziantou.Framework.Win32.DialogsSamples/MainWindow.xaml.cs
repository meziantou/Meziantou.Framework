using System.Windows;
using Meziantou.Framework.Win32.Dialogs;

namespace Meziantou.Framework.Win32.DialogsSamples
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void ButtonOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            dialog.Title = "Sample Open Folder dialog";
            dialog.OkButtonLabel = "Test OK";
            dialog.ShowDialog();
        }
    }
}
