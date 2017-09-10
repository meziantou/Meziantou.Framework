using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.Csv
{
    public class CsvReader
    {
        public const char DefaultSeparatorCharacter = ',';
        public const char DefaultQuoteCharacter = '"';
        
        private CsvColumn[] _columns;

        public char Separator { get; set; } = DefaultSeparatorCharacter;
        public char? Quote { get; set; } = DefaultQuoteCharacter;
        public bool HasHeaderRow { get; set; } = true;

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
            if (textReader == null) throw new ArgumentNullException(nameof(textReader));

            BaseReader = textReader;
        }

        public CsvRow ReadRow()
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
                    var i = BaseReader.Read();
                    if (i < 0)
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
                    char c = (char)i;
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
                                value.Append(c);
                                c = (char)BaseReader.Read();
                                ColumnNumber++;
                            }
                            LineNumber++;
                        }

                        if (c == Quote)
                        {
                            if (next == Quote)
                            {
                                BaseReader.Read();
                                hasCell = true;
                                value.Append(Quote);
                            }
                            else
                            {
                                inQuote = false;
                            }
                        }
                        else
                        {
                            hasCell = true;
                            value.Append(c);
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
                    else if (c == Quote)
                    {
                        if (next == Quote)
                        {
                            BaseReader.Read();
                            hasCell = true;
                            value.Append(Quote);
                        }
                        else
                        {
                            inQuote = true;
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
                        value.Append(c);
                    }
                }
            }

            if (rowValues.Count == 0 && endOfStream)
                return null;

            if (HasHeaderRow && _columns == null)
            {
                _columns = rowValues.Select((value, index) => new CsvColumn(value, index)).ToArray();
                return ReadRow(); // Read the first row with data
            }

            return new CsvRow(_columns, rowValues);
        }
    }
}
