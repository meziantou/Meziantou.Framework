using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using Meziantou.Framework.WPF.Collections;

namespace Meziantou.Framework.WPF.CollectionSamples
{
    public class SampleViewModel
    {
        public SampleViewModel()
        {
            AddItems = new DelegateCommand(AddItemsImpl);
        }

        public ConcurrentObservableCollection<string> Items { get; } = new ConcurrentObservableCollection<string>();

        public ICommand AddItems { get; }

        private void AddItemsImpl()
        {
            Task.Run(() => Parallel.For(Items.Count, Items.Count + 1000, i => Items.Add(i.ToString(CultureInfo.InvariantCulture))));
        }
    }
}
