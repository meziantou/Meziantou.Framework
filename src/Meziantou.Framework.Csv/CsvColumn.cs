#nullable disable
namespace Meziantou.Framework.Csv
{
    public class CsvColumn
    {
        public CsvColumn(int index)
        {
            Index = index;
        }

        public CsvColumn(string name, int index)
        {
            Index = index;
            Name = name;
        }

        public string Name { get; }
        public int Index { get; }
    }
}
