using System.Windows.Controls;
using Meziantou.Framework.Collections.Concurrent;

namespace Meziantou.Framework.WPF.CollectionSamples;

/// <summary>Examples of the Meziantou.Framework.Threading readme, compiled in CI but not run.</summary>
public static class ConcurrentObservableCollectionReadmeExamples
{
    public static async Task BindToListBoxAsync(ListBox listBox)
    {
        #region ConcurrentObservableCollection_Bind
        // Created on the UI thread, so the notifications are raised on that thread
        var collection = new ConcurrentObservableCollection<string>();
        listBox.ItemsSource = collection.AsObservable;

        // The collection itself is safe to use from any thread
        await Task.Run(() => collection.Add("Item from a background thread"));
        #endregion
    }
}
