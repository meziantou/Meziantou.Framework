using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tomlyn.Model
{
    /// <summary>
    /// Runtime representation of a TOML table
    /// </summary>
    /// <remarks>
    /// This object keep the order of the inserted key=values
    /// </remarks>
    public sealed class TomlTable : TomlObject, IDictionary<string, object>
    {
        private const int MapCreationThreshold = 8;

        private readonly List<Entry> _order;
        private Dictionary<string, int>? _map;

        /// <summary>
        /// Creates an instance of a <see cref="TomlTable"/>
        /// </summary>
        public TomlTable() : this(false)
        {
        }

        /// <summary>
        /// Creates an instance of <see cref="TomlTable"/>.
        /// </summary>
        /// <param name="inline"></param>
        public TomlTable(bool inline) : base(inline ? ObjectKind.InlineTable : ObjectKind.Table)
        {
            _order = new List<Entry>();
            _map = null;
        }

        internal TomlPropertiesMetadata? PropertiesMetadata { get; set; }

        /// <summary>
        /// Returns an enumerator that iterates through the table.
        /// </summary>
        /// <returns>An enumerator for the table.</returns>
        public Enumerator GetEnumerator() => new Enumerator(_order);

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void ICollection<KeyValuePair<string, object>>.Add(KeyValuePair<string, object> item)
        {
            Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _order.Clear();
            _map?.Clear();
            _map = null;
        }

        bool ICollection<KeyValuePair<string, object>>.Contains(KeyValuePair<string, object> item)
        {
            if (_map is not null)
            {
                if (_map.TryGetValue(item.Key, out var index))
                {
                    return _order[index].Value == item.Value;
                }

                return false;
            }

            var linearIndex = IndexOfKey(item.Key);
            return linearIndex >= 0 && _order[linearIndex].Value == item.Value;
        }

        void ICollection<KeyValuePair<string, object>>.CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            if (arrayIndex + _order.Count > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            for (var i = 0; i < _order.Count; i++)
            {
                var item = _order[i];
                array[i + arrayIndex] = new KeyValuePair<string, object>(item.Key, item.Value);
            }
        }

        bool ICollection<KeyValuePair<string, object>>.Remove(KeyValuePair<string, object> item)
        {
            if (_map is not null)
            {
                if (_map.TryGetValue(item.Key, out var index) && _order[index].Value == item.Value)
                {
                    return Remove(item.Key);
                }

                return false;
            }

            var linearIndex = IndexOfKey(item.Key);
            if (linearIndex < 0 || _order[linearIndex].Value != item.Value)
            {
                return false;
            }

            _order.RemoveAt(linearIndex);
            return true;
        }

        /// <inheritdoc />
        public int Count => _map?.Count ?? _order.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public void Add(string key, object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_map is not null)
            {
                _map.Add(key, _order.Count);
                _order.Add(new Entry(key, value));
                return;
            }

            EnsureKeyDoesNotExist(key);
            _order.Add(new Entry(key, value));

            if (_order.Count > MapCreationThreshold)
            {
                EnsureMap();
            }
        }

        /// <inheritdoc />
        public bool ContainsKey(string key)
        {
            if (_map is not null)
            {
                return _map.ContainsKey(key);
            }

            return IndexOfKey(key) >= 0;
        }

        /// <inheritdoc />
        public bool Remove(string key)
        {
            if (_map is null)
            {
                var linearIndex = IndexOfKey(key);
                if (linearIndex < 0)
                {
                    return false;
                }

                _order.RemoveAt(linearIndex);
                return true;
            }

            if (_map.TryGetValue(key, out var index))
            {
                _map.Remove(key);
                _order.RemoveAt(index);

                for (var i = index; i < _order.Count; i++)
                {
                    _map[_order[i].Key] = i;
                }

                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public bool TryGetValue(string key, out object value)
        {
            value = null!;

            if (_map is not null)
            {
                if (_map.TryGetValue(key, out var index))
                {
                    value = _order[index].Value;
                    return true;
                }

                return false;
            }

            var linearIndex = IndexOfKey(key);
            if (linearIndex >= 0)
            {
                value = _order[linearIndex].Value;
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public object this[string key]
        {
            get
            {
                if (_map is not null)
                {
                    return _order[_map[key]].Value;
                }

                var index = IndexOfKey(key);
                if (index < 0)
                {
                    throw new KeyNotFoundException();
                }

                return _order[index].Value;
            }
            set
            {
                // If the key exists, update it without changing insertion order.
                if (_map is not null)
                {
                    ref var indexRef = ref CollectionsMarshal.GetValueRefOrAddDefault(_map, key, out var exists);
                    if (exists)
                    {
                        var entry = _order[indexRef];
                        entry.Value = value;
                        _order[indexRef] = entry;
                    }
                    else
                    {
                        indexRef = _order.Count;
                        _order.Add(new Entry(key, value));
                    }

                    return;
                }

                var linearIndex = IndexOfKey(key);
                if (linearIndex >= 0)
                {
                    var entry = _order[linearIndex];
                    entry.Value = value;
                    _order[linearIndex] = entry;
                    return;
                }

                _order.Add(new Entry(key, value));
                if (_order.Count > MapCreationThreshold)
                {
                    EnsureMap();
                }
            }
        }

        /// <inheritdoc />
        public ICollection<string> Keys
        {
            get
            {
                var list = new List<string>(_order.Count);
                for (var i = 0; i < _order.Count; i++)
                {
                    list.Add(_order[i].Key);
                }
                return list;
            }
        }

        /// <inheritdoc />
        public ICollection<object> Values
        {
            get
            {
                var list = new List<object>(_order.Count);
                for (var i = 0; i < _order.Count; i++)
                {
                    list.Add(_order[i].Value);
                }
                return list;
            }
        }

        internal struct Entry
        {
            public Entry(string key, object value)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; }

            public object Value { get; set; }
        }

        private void EnsureKeyDoesNotExist(string key)
        {
            if (IndexOfKey(key) >= 0)
            {
                throw new ArgumentException("An item with the same key has already been added.", nameof(key));
            }
        }

        private int IndexOfKey(string key)
        {
            for (var i = 0; i < _order.Count; i++)
            {
                if (string.Equals(_order[i].Key, key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureMap()
        {
            if (_map is not null)
            {
                return;
            }

            var map = new Dictionary<string, int>(_order.Count, StringComparer.Ordinal);
            for (var i = 0; i < _order.Count; i++)
            {
                map.Add(_order[i].Key, i);
            }

            _map = map;
        }

        /// <summary>
        /// Enumerates the key-value pairs contained in a <see cref="TomlTable"/>.
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private List<Entry>.Enumerator _enumerator;

            internal Enumerator(List<Entry> order)
            {
                _enumerator = order.GetEnumerator();
            }

            /// <inheritdoc />
            public KeyValuePair<string, object> Current
            {
                get
                {
                    var current = _enumerator.Current;
                    return new KeyValuePair<string, object>(current.Key, current.Value);
                }
            }

            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public bool MoveNext() => _enumerator.MoveNext();

            /// <inheritdoc />
            public void Reset() => throw new NotSupportedException();

            /// <inheritdoc />
            public void Dispose() => _enumerator.Dispose();
        }
    }
}
