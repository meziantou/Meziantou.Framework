using System.Collections;
using System.Runtime.InteropServices;

namespace Meziantou.Framework
{
    [StructLayout(LayoutKind.Auto)]
    [Obsolete("Use System.HashCode")]
    public struct HashCodeCombiner
    {
        private int _hash;

        public readonly int HashCode => _hash.GetHashCode();

        public static implicit operator int(HashCodeCombiner hashCodeCombiner) => hashCodeCombiner.HashCode;

        private void Add(int i)
        {
            _hash = (_hash * 397) ^ i;
        }

        public void Add(object? o)
        {
            var hashCode = o != null ? o.GetHashCode() : 0;
            Add(hashCode);
        }

        public void Add<TValue>(TValue value, IEqualityComparer<TValue> comparer)
        {
            var hashCode = value != null ? comparer.GetHashCode(value) : 0;
            Add(hashCode);
        }

        public void Add(IEnumerable? e)
        {
            if (e == null)
            {
                Add(0);
            }
            else
            {
                var count = 0;
                foreach (var o in e)
                {
                    Add(o);
                    count++;
                }
                Add(count);
            }
        }
    }
}
