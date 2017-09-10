using System;
using System.Collections.Generic;
using System.Linq;

namespace Meziantou.Framework.Csv
{
    public class CsvRow
    {
        public IReadOnlyList<CsvColumn> Columns { get; }
        public IReadOnlyList<string> Values { get; }

        public CsvRow(IReadOnlyList<CsvColumn> columns, IReadOnlyList<string> values)
        {
            Columns = columns;
            Values = values;
        }

        public string GetValue(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (index >= Values.Count)
                return null;

            return Values[index];
        }

        public string GetValue(string columnName)
        {
            var column = Columns.FirstOrDefault(c => c.Name == columnName);
            return GetValue(column);
        }

        public string GetValue(CsvColumn column)
        {
            return GetValue(column.Index);
        }
    }
}
