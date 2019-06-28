using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Meziantou.Framework.Windows.Collections
{
    public interface IReadOnlyObservableCollection<T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {
    }
}
