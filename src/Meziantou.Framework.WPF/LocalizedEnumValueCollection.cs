using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Meziantou.Framework.WPF;

internal sealed class LocalizedEnumValueCollection : ReadOnlyCollection<LocalizedEnumValue>
{
    public LocalizedEnumValueCollection(IList<LocalizedEnumValue> list)
        : base(list)
    {
    }

    public LocalizedEnumValue this[object value] => this.First(_ => _.Value.Equals(value));
}
