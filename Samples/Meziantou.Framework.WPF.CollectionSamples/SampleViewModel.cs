using System.Globalization;
using System.Windows.Input;
using Meziantou.Framework.WPF.Collections;

namespace Meziantou.Framework.WPF.CollectionSamples;

public sealed class SampleViewModel
{
    public SampleViewModel()
    {
        AddItems = DelegateCommand.Create(AddItemsImpl);
    }

    public ConcurrentObservableCollection<string> Items { get; } = [];

    public ICommand AddItems { get; }

    private void AddItemsImpl()
    {
        _ = Task.Run(() => Parallel.For(Items.Count, Items.Count + 1000, i => Items.Add(i.ToString(CultureInfo.InvariantCulture))));
    }
}
