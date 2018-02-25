using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Meziantou.Framework.Csv.Tests
{
    [TestClass]
    public class CsvWriterTests
    {
        [TestMethod]
        public async Task CsvWriterAsync_NoEscape()
        {
            using (var sw = new StringWriter())
            {
                var writer = new CsvWriter(sw);
                await writer.WriteRowAsync("A", "B");
                await writer.WriteRowAsync("C", "D");

                Assert.AreEqual(@"A,B
C,D", sw.ToString());
            }
        }

        [TestMethod]
        public async Task CsvWriterAsync_EscapeValueWithSeparator()
        {
            using (var sw = new StringWriter())
            {
                var writer = new CsvWriter(sw);
                await writer.WriteRowAsync("A", "B,");
                await writer.WriteRowAsync("C", "D");

                Assert.AreEqual(@"A,""B,""
C,D", sw.ToString());
            }
        }

        [TestMethod]
        public async Task CsvWriterAsync_EscapeValueWithStartingQuote()
        {
            using (var sw = new StringWriter())
            {
                var writer = new CsvWriter(sw);
                await writer.WriteRowAsync("A", "\"B");

                Assert.AreEqual("A,\"\"\"B\"", sw.ToString());
            }
        }

        [TestMethod]
        [DataRow("A;B:D;E")]
        [DataRow("A,;B:D;E")]
        [DataRow(",A;B:D;E")]
        [DataRow("A;\"B:D;E")]
        [DataRow("A;B\":D;E")]
        public async Task CsvWriterAsync_CsvReader(string data)
        {
            var rows = new List<List<string>>();
            foreach (var row in data.Split(':'))
            {
                rows.Add(new List<string>(row.Split(';')));
            }

            using (var sw = new StringWriter())
            {
                var writer = new CsvWriter(sw);
                foreach (var row in rows)
                {
                    await writer.WriteRowAsync(row);
                }

                var csv = sw.ToString();
                using (var sr = new StringReader(csv))
                {
                    var reader = new CsvReader(sr);

                    var rowIndex = -1;
                    CsvRow csvRow;
                    while ((csvRow = await reader.ReadRowAsync()) != null)
                    {
                        rowIndex++;
                        CollectionAssert.AreEqual(rows[rowIndex], csvRow.Values.ToList());
                    }

                    Assert.AreEqual(rows.Count - 1, rowIndex);
                }
            }
        }
    }
}
