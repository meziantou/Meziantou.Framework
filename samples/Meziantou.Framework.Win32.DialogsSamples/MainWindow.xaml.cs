using System.Runtime.Versioning;
using System.Windows;

namespace Meziantou.Framework.Win32.DialogsSamples;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    [SupportedOSPlatform("windows6.0.6000")]
    private void ButtonOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        #region OpenFolderDialog_Owner
        var dialog = new OpenFolderDialog
        {
            Title = "Sample Open Folder dialog",
            OkButtonLabel = "Select Folder",
        };

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var result = dialog.ShowDialog(hwnd);
        #endregion

        if (result == Win32.DialogResult.OK)
        {
            MessageBox.Show(this, dialog.SelectedPath, "Selected folder");
        }
    }

    [SupportedOSPlatform("windows6.0.6000")]
    private void ButtonBasicUsage_Click(object sender, RoutedEventArgs e) => ReadmeExamples.BasicUsage();

    [SupportedOSPlatform("windows6.0.6000")]
    private void ButtonAdvancedUsage_Click(object sender, RoutedEventArgs e) => ReadmeExamples.AdvancedUsage();
}
