using System.Text;

namespace Meziantou.Framework.Csv
{
    public class CsvReader
    {
        public const char DefaultSeparatorCharacter = ',';
        public const char DefaultQuoteCharacter = '"';

        private CsvColumn[]? _columns;
        private readonly char[] _readBuffer = new char[1];

        public char Separator { get; set; } = DefaultSeparatorCharacter;
        public char? Quote { get; set; } = DefaultQuoteCharacter;
        public bool HasHeaderRow { get; set; }

        public int ColumnNumber { get; private set; }
        public int LineNumber { get; private set; }
        public int RowNumber { get; set; }
        public TextReader BaseReader { get; }

        public bool EndOfStream
        {
            get
            {
                if (BaseReader is StreamReader streamReader)
                    return streamReader.EndOfStream;

                return BaseReader.Peek() < 0;
            }
        }

        public CsvReader(TextReader textReader)
        {
            BaseReader = textReader ?? throw new ArgumentNullException(nameof(textReader));
        }

        private async Task<char?> ReadCharAsync()
        {
            var buffer = _readBuffer;
            var readCount = await BaseReader.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
            if (readCount <= 0)
                return null;

            return buffer[0];
        }

        public async Task<CsvRow?> ReadRowAsync()
        {
            var endOfStream = false;
            var rowValues = new List<string>();
            if (BaseReader != null)
            {
                var inQuote = false;
                var value = new StringBuilder();
                var hasCell = false;
                ColumnNumber = 0;
                while (true)
                {
                    var c = await ReadCharAsync().ConfigureAwait(false);
                    if (!c.HasValue)
                    {
                        endOfStream = true;
                        if (hasCell)
                        {
                            rowValues.Add(value.ToString());
                        }

                        RowNumber++;
                        break;
                    }

                    ColumnNumber++;
                    var next = BaseReader.Peek();
                    if (inQuote)
                    {
                        if (c == '\n')
                        {
                            LineNumber++;
                        }
                        else if (c == '\r')
                        {
                            if (next == '\n')
                            {
                                hasCell = true;
                                value.Append(c.Value);
                                c = await ReadCharAsync().ConfigureAwait(false);
                                ColumnNumber++;
                            }
                            LineNumber++;
                        }

                        if (Quote.HasValue && c == Quote)
                        {
                            if (next == Quote)
                            {
                                await ReadCharAsync().ConfigureAwait(false);
                                hasCell = true;
                                value.Append(Quote.Value);
                            }
                            else
                            {
                                inQuote = false;
                            }
                        }
                        else
                        {
                            hasCell = true;
                            value.Append(c.Value);
                        }
                    }
                    else if (c == '\n')
                    {
                        if (hasCell)
                        {
                            rowValues.Add(value.ToString());
                        }

                        LineNumber++;
                        break;
                    }
                    else if (c == '\r')
                    {
                        if (next == '\n')
                        {
                            BaseReader.Read();
                            ColumnNumber++;
                        }

                        if (hasCell)
                        {
                            rowValues.Add(value.ToString());
                        }

                        LineNumber++;
                        break;
                    }
                    else if (Quote.HasValue && c == Quote)
                    {
                        if (value.Length == 0)
                        {
                            inQuote = true;
                        }
                        else
                        {
                            hasCell = true;
                            value.Append(c.Value);
                        }
                    }
                    else if (c == Separator)
                    {
                        rowValues.Add(value.ToString());
                        value.Clear();
                        hasCell = false;
                    }
                    else
                    {
                        hasCell = true;
                        value.Append(c.Value);
                    }
                }
            }

            if (rowValues.Count == 0 && endOfStream)
                return null;

            var columns = _columns;
            if (HasHeaderRow && columns == null)
            {
                columns = rowValues.Select(CreateColumn).ToArray();
                _columns = columns;
                return await ReadRowAsync().ConfigureAwait(false); // Read the first row with data
            }

            return CreateRow(columns, rowValues);
        }

        protected virtual CsvColumn CreateColumn(string name, int index)
        {
            return new CsvColumn(name, index);
        }

        protected virtual CsvRow CreateRow(IReadOnlyList<CsvColumn>? columns, IReadOnlyList<string> values)
        {
            return new CsvRow(columns, values);
        }
    }
}
