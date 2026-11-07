using System.Windows.Controls;
using Meziantou.Framework.Collections.Concurrent;

namespace Meziantou.Framework.WPF.CollectionSamples;

public static class ConcurrentObservableCollectionSamples
{
    public static async Task BindToListBox(ListBox myListBox)
    {
        #region ConcurrentObservableCollection_BindToListBox
        var collection = new ConcurrentObservableCollection<string>();
        myListBox.ItemsSource = collection.AsObservable;

        // Safe to call from any thread
        await Task.Run(() => collection.Add("Item 1"));
        #endregion
    }
}
