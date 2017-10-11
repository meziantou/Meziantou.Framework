using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Meziantou.Framework.Csv
{
    public class CsvRow : IReadOnlyDictionary<string, string>
    {
        public IReadOnlyList<CsvColumn> Columns { get; }
        public IReadOnlyList<string> Values { get; }

        public CsvRow(IReadOnlyList<CsvColumn> columns, IReadOnlyList<string> values)
        {
            Columns = columns;
            Values = values;
        }

        public virtual string this[int index]
        {
            get
            {
                if (index < 0)
                    throw new ArgumentOutOfRangeException(nameof(index));

                if (index >= Values.Count)
                    return null;

                return Values[index];
            }
        }

        public virtual string this[string columnName]
        {
            get
            {
                if (columnName == null) throw new ArgumentNullException(nameof(columnName));

                var column = Columns.FirstOrDefault(c => c.Name == columnName);
                return this[column];
            }
        }

        public virtual string this[CsvColumn column]
        {
            get
            {
                if (column == null) throw new ArgumentNullException(nameof(column));

                return this[column.Index];
            }
        }

        IEnumerable<string> IReadOnlyDictionary<string, string>.Keys => Columns.Select(c => c.Name);

        IEnumerable<string> IReadOnlyDictionary<string, string>.Values => Values;

        int IReadOnlyCollection<KeyValuePair<string, string>>.Count => Values.Count;

        string IReadOnlyDictionary<string, string>.this[string key] => this[key];

        bool IReadOnlyDictionary<string, string>.ContainsKey(string key)
        {
            return Columns.Any(c => c.Name == key);
        }

        bool IReadOnlyDictionary<string, string>.TryGetValue(string key, out string value)
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

        IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
        {
            return Columns.Select(c => new KeyValuePair<string, string>(c.Name, this[c])).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Values.GetEnumerator();
        }
    }
}
