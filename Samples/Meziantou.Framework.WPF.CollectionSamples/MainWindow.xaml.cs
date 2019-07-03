using System.Threading.Tasks;
using System.Windows;
using Meziantou.Framework.WPF.Collections;

namespace Meziantou.Framework.WPF.CollectionSamples
{
    public partial class MainWindow : Window
    {
        private readonly ThreadSafeCollection<string> _items = new ThreadSafeCollection<string>();

        public MainWindow()
        {
            InitializeComponent();
            Lbx.ItemsSource = _items.AsObservable;
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => Parallel.For(_items.Count, _items.Count + 1000, i => _items.Add(i.ToString())));
        }
    }
}
