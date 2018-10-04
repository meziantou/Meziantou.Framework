using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace Meziantou.Framework.Csv.Tests
{
    [TestClass]
    public class CsvRowTests
    {
        [TestMethod]
        public void GetValueExtensionIsAvailable()
        {
            // Arrange
            var columns = new List<CsvColumn> { new CsvColumn("test", 0) };
            var values = new List<string> { "42" };
            var row = new CsvRow(columns, values);

            // Act
            var actual = row.GetValueOrDefault("test", 0);

            // Assert
            Assert.AreEqual(42, actual);
        }
    }
}
