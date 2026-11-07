using System.Windows;
using Meziantou.Framework.Collections.Concurrent;

namespace Meziantou.Framework.WPF.CollectionSamples;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => _ = FillListBoxAsync();

    // Example of the readme (src/Meziantou.Framework.WPF/readme.md)
    private async Task FillListBoxAsync()
    {
        #region ConcurrentObservableCollection_ListBox
        var collection = new ConcurrentObservableCollection<string>();
        listBox.ItemsSource = collection.AsObservable;

        await Task.Run(() => collection.Add("Item from background thread"));
        #endregion
    }
}
