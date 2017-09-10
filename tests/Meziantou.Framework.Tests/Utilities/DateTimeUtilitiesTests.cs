using System;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class DateTimeUtilitiesTests
    {
        [TestMethod]
        public void StartOfWeek_01()
        {
            // Arrange
            var dt = new DateTime(2015, 05, 17);

            // Act
            var actual = dt.StartOfWeek(DayOfWeek.Sunday);

            // Assert
            var expected = new DateTime(2015, 05, 17);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void StartOfWeek_02()
        {
            // Arrange
            var dt = new DateTime(2015, 05, 17);

            // Act
            var actual = dt.StartOfWeek(DayOfWeek.Monday);

            // Assert
            var expected = new DateTime(2015, 05, 11);
            Assert.AreEqual(expected, actual);
        }
        
        [TestMethod]
        public void FirstDateOfWeekIso8601_01()
        {
            // Arrange
            var year = 2005;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2005, 01, 03);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void FirstDateOfWeekIso8601_02()
        {
            // Arrange
            var year = 2005;
            var week = 52;
            
            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2005, 12, 26);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void FirstDateOfWeekIso8601_03()
        {
            // Arrange
            var year = 2006;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2006, 1, 2);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void FirstDateOfWeekIso8601_04()
        {
            // Arrange
            var year = 2006;
            var week = 52;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2006, 12, 25);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void FirstDateOfWeekIso8601_05()
        {
            // Arrange
            var year = 2008;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2007, 12, 31);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void FirstDateOfWeekIso8601_06()
        {
            // Arrange
            var year = 2008;
            var week = 52;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2008, 12, 22);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void FirstDateOfWeekIso8601_07()
        {
            // Arrange
            var year = 2009;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2008, 12, 29);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void FirstDateOfWeekIso8601_08()
        {
            // Arrange
            var year = 2009;
            var week = 53;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2009, 12, 28);
            Assert.AreEqual(expected, actual);
        }
    }
}
