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
        var dialog = new OpenFolderDialog
        {
            Title = "Sample Open Folder dialog",
            OkButtonLabel = "Test OK",
        };
        dialog.ShowDialog();
    }
}
