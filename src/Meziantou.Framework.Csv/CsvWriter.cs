using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Meziantou.Framework.Csv
{
    public class CsvWriter
    {
        private bool _isFirstRow = true;
        private bool _isFirstRowValue = true;

        public TextWriter BaseWriter { get; }
        public string EndOfLine { get; set; } = Environment.NewLine;
        public char Separator { get; set; } = CsvReader.DefaultSeparatorCharacter;
        public char? Quote { get; set; } = CsvReader.DefaultQuoteCharacter;

        public CsvWriter(TextWriter textReader)
        {
            BaseWriter = textReader ?? throw new ArgumentNullException(nameof(textReader));
        }

        public Task BeginRowAsync()
        {
            var isFirstRow = _isFirstRow;
            _isFirstRow = false;
            _isFirstRowValue = true;
            if (!isFirstRow)
            {
                return BaseWriter.WriteAsync(EndOfLine);
            }

            return Task.CompletedTask;
        }

        public Task WriteValueAsync(string value)
        {
            var sb = new StringBuilder();
            if (!_isFirstRowValue)
            {
                sb.Append(Separator);
            }

            _isFirstRowValue = false;
            if (!string.IsNullOrEmpty(value))
            {
                if (Quote.HasValue)
                {
                    var quote = Quote.Value;
                    if (value[0] != quote && value.IndexOf(Separator) < 0)
                    {
                        sb.Append(value);
                    }
                    else
                    {
                        sb.Append(quote);
                        for (var i = 0; i < value.Length; i++)
                        {
                            var c = value[i];
                            if (c == quote)
                            {
                                sb.Append(quote);
                                sb.Append(quote);
                            }
                            else
                            {
                                sb.Append(c);
                            }
                        }

                        sb.Append(Quote);
                    }
                }
                else
                {
                    sb.Append(value);
                }
            }

            return BaseWriter.WriteAsync(sb.ToString());
        }

        public async Task WriteValuesAsync(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                await WriteValueAsync(value).ConfigureAwait(false);
            }
        }

        public Task WriteValuesAsync(params string[] values)
        {
            return WriteValuesAsync((IEnumerable<string>)values);
        }

        public Task WriteRowAsync(params string[] values)
        {
            return WriteRowAsync((IEnumerable<string>)values);
        }

        public async Task WriteRowAsync(IEnumerable<string> values)
        {
            await BeginRowAsync().ConfigureAwait(false);
            await WriteValuesAsync(values).ConfigureAwait(false);
        }
    }
}
