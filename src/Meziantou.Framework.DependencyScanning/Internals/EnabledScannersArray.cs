using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct EnabledScannersArray : IEnabledScannersArray
    {
        private SortedList<int, int>? _values;

        [MemberNotNullWhen(returnValue: false, nameof(_values))]
        public bool IsEmpty => _values is null;

        public void Set(int index)
        {
            if (_values is null)
            {
                _values = new SortedList<int, int>();
            }

            _values.Add(index, index);
        }

        public bool Get(int index)
        {
            if (_values is null)
                return false;

            return _values.ContainsKey(index);
        }
    }
}
