using System.Collections.Specialized;
using System.ComponentModel;

namespace Meziantou.Framework.WPF.Collections
{
    public interface IReadOnlyObservableCollection<T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {
    }
}
