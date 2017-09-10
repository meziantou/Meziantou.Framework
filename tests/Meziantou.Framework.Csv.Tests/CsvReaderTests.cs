using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;

namespace Meziantou.Framework.Csv.Tests
{
    [TestClass]
    public class CsvReaderTests
    {
        [TestMethod]
        public void CsvReader_RowWithoutHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("value1.1,value1.2,value1.3");
            sb.Append("value2.1,value2.2,value2.3");

            using (var sr = new StringReader(sb.ToString()))
            {
                var reader = new CsvReader(sr);
                reader.HasHeaderRow = false;

                var row1 = reader.ReadRow();
                var row2 = reader.ReadRow();
                var row3 = reader.ReadRow();

                Assert.IsNull(row3);

                Assert.AreEqual("value1.1", row1.GetValue(0));
                Assert.AreEqual("value1.2", row1.GetValue(1));
                Assert.AreEqual("value1.3", row1.GetValue(2));

                Assert.AreEqual("value2.1", row2.GetValue(0));
                Assert.AreEqual("value2.2", row2.GetValue(1));
                Assert.AreEqual("value2.3", row2.GetValue(2));
            }
        }

        [TestMethod]
        public void CsvReader_RowWithHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("column1,column2,column3");
            sb.AppendLine("value1.1,value1.2,value1.3");
            sb.Append("value2.1,value2.2,value2.3");

            using (var sr = new StringReader(sb.ToString()))
            {
                var reader = new CsvReader(sr);
                var row1 = reader.ReadRow();
                var row2 = reader.ReadRow();
                var row3 = reader.ReadRow();

                Assert.IsNull(row3);

                Assert.AreEqual("value1.1", row1.GetValue("column1"));
                Assert.AreEqual("value1.2", row1.GetValue("column2"));
                Assert.AreEqual("value1.3", row1.GetValue("column3"));

                Assert.AreEqual("value2.1", row2.GetValue("column1"));
                Assert.AreEqual("value2.2", row2.GetValue("column2"));
                Assert.AreEqual("value2.3", row2.GetValue("column3"));
            }
        }

        [TestMethod]
        public void CsvReader_MultiLineQuotedValue()
        {
            var sb = new StringBuilder();
            sb.AppendLine("column1,column2,column3");
            sb.AppendLine("value1.1,\"value1.2\r\nline2\",value1.3");
            sb.Append("value2.1,value2.2,value2.3");

            using (var sr = new StringReader(sb.ToString()))
            {
                var reader = new CsvReader(sr);
                var row1 = reader.ReadRow();
                var row2 = reader.ReadRow();
                var row3 = reader.ReadRow();

                Assert.IsNull(row3);

                Assert.AreEqual("value1.1", row1.GetValue("column1"));
                Assert.AreEqual("value1.2\r\nline2", row1.GetValue("column2"));
                Assert.AreEqual("value1.3", row1.GetValue("column3"));

                Assert.AreEqual("value2.1", row2.GetValue("column1"));
                Assert.AreEqual("value2.2", row2.GetValue("column2"));
                Assert.AreEqual("value2.3", row2.GetValue("column3"));
            }
        }
    }
}
