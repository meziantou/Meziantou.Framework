﻿using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Meziantou.Framework.Csv.Tests
{
    public class CsvWriterTests
    {
        [Fact]
        public async Task CsvWriterAsync_NoEscape()
        {
            using var sw = new StringWriter();
            var writer = new CsvWriter(sw);
            await writer.WriteRowAsync("A", "B").ConfigureAwait(false);
            await writer.WriteRowAsync("C", "D").ConfigureAwait(false);

            Assert.Equal($"A,B{Environment.NewLine}C,D", sw.ToString());
        }

        [Fact]
        public async Task CsvWriterAsync_EscapeValueWithSeparator()
        {
            using var sw = new StringWriter();
            var writer = new CsvWriter(sw);
            await writer.WriteRowAsync("A", "B,").ConfigureAwait(false);
            await writer.WriteRowAsync("C", "D").ConfigureAwait(false);

            Assert.Equal($@"A,""B,""{Environment.NewLine}C,D", sw.ToString());
        }

        [Fact]
        public async Task CsvWriterAsync_EscapeValueWithStartingQuote()
        {
            using var sw = new StringWriter();
            var writer = new CsvWriter(sw);
            await writer.WriteRowAsync("A", "\"B").ConfigureAwait(false);

            Assert.Equal("A,\"\"\"B\"", sw.ToString());
        }

        [Fact]
        public async Task CsvWriterAsync_WriteValues()
        {
            using var sw = new StringWriter();
            var writer = new CsvWriter(sw);
            writer.EndOfLine = "\n";
            await writer.BeginRowAsync().ConfigureAwait(false);
            await writer.WriteValuesAsync("A", "B").ConfigureAwait(false);
            await writer.WriteValuesAsync("C", "D").ConfigureAwait(false);
            await writer.BeginRowAsync().ConfigureAwait(false);
            await writer.WriteValuesAsync("E").ConfigureAwait(false);

            Assert.Equal("A,B,C,D\nE", sw.ToString());
        }

        [Fact]
        public async Task CsvWriterAsync_NoQuoteCharacter()
        {
            using var sw = new StringWriter();
            var writer = new CsvWriter(sw);
            writer.Quote = null;

            await writer.WriteRowAsync("A\"", "B").ConfigureAwait(false);

            Assert.Equal("A\",B", sw.ToString());
        }

        [Theory]
        [InlineData("A;B:D;E")]
        [InlineData("A,;B:D;E")]
        [InlineData(",A;B:D;E")]
        [InlineData("A;\"B:D;E")]
        [InlineData("A;B\":D;E")]
        public async Task CsvWriterAsync_CsvReader(string data)
        {
            var rows = new List<List<string>>();
            foreach (var row in data.Split(':'))
            {
                rows.Add(new List<string>(row.Split(';')));
            }

            using var sw = new StringWriter();
            var writer = new CsvWriter(sw);
            foreach (var row in rows)
            {
                await writer.WriteRowAsync(row).ConfigureAwait(false);
            }

            var csv = sw.ToString();
            using var sr = new StringReader(csv);
            var reader = new CsvReader(sr);

            var rowIndex = -1;
            CsvRow csvRow;
            while ((csvRow = await reader.ReadRowAsync().ConfigureAwait(false)) != null)
            {
                rowIndex++;
                Assert.Equal(rows[rowIndex], csvRow.Values.ToList());
            }

            Assert.Equal(rows.Count - 1, rowIndex);
        }
    }
}
