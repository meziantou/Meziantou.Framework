using System.Collections.Specialized;
using System.ComponentModel;

namespace Meziantou.Framework.WPF.Collections;

/// <summary>Represents a read-only observable collection that provides notifications when items are added, removed, or the collection is refreshed.</summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public interface IReadOnlyObservableCollection<T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
}
