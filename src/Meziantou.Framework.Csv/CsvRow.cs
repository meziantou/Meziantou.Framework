using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Meziantou.Framework.Csv
{
    public class CsvRow : IReadOnlyDictionary<string, string?>
    {
        public IReadOnlyList<CsvColumn>? Columns { get; }
        public IReadOnlyList<string> Values { get; }

        internal CsvRow(IReadOnlyList<CsvColumn>? columns, IReadOnlyList<string> values)
        {
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Columns = columns;
        }

        public virtual string? this[int index]
        {
            get
            {
                if (index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));

                if (index >= Values.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return Values[index];
            }
        }

        public virtual string? this[string columnName]
        {
            get
            {
                if (columnName == null)
                    throw new ArgumentNullException(nameof(columnName));

                if (Columns == null)
                    throw new InvalidOperationException("Columns are not parsed");

                var column = Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.Ordinal));
                if (column == null)
                    return null;

                return this[column];
            }
        }

        public virtual string? this[CsvColumn column]
        {
            get
            {
                if (column == null)
                    throw new ArgumentNullException(nameof(column));

                return this[column.Index];
            }
        }

        IEnumerable<string> IReadOnlyDictionary<string, string?>.Keys => Columns == null ? Enumerable.Empty<string>() : Columns.Where(c => c.Name != null).Select(c => c.Name!);

        IEnumerable<string> IReadOnlyDictionary<string, string?>.Values => Values;

        int IReadOnlyCollection<KeyValuePair<string, string?>>.Count => Values.Count;

        string? IReadOnlyDictionary<string, string?>.this[string key] => this[key];

        bool IReadOnlyDictionary<string, string?>.ContainsKey(string key)
        {
            if (Columns == null)
                return false;

            return Columns.Any(c => string.Equals(c.Name, key, StringComparison.Ordinal));
        }

        bool IReadOnlyDictionary<string, string?>.TryGetValue(string key, out string? value)
        {
            var v = this[key];
            if (v != null)
            {
                value = v;
                return true;
            }

            value = default;
            return false;
        }

        IEnumerator<KeyValuePair<string, string?>> IEnumerable<KeyValuePair<string, string?>>.GetEnumerator()
        {
            if (Columns == null)
                return Values.Select(v => new KeyValuePair<string, string?>(key: "", v)).GetEnumerator();

            return Columns.Select(c => new KeyValuePair<string, string?>(c.Name ?? "", this[c])).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Values.GetEnumerator();
        }
    }
}
