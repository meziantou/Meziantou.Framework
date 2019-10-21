﻿#pragma warning disable CS0618 // Type or member is obsolete

using System;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DateTimeUtilitiesTests
    {
        [Fact]
        public void StartOfWeek_01()
        {
            // Arrange
            var dt = new DateTime(2015, 05, 17);

            // Act
            var actual = DateTimeUtilities.StartOfWeek(dt, DayOfWeek.Sunday);

            // Assert
            var expected = new DateTime(2015, 05, 17);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void StartOfWeek_02()
        {
            // Arrange
            var dt = new DateTime(2015, 05, 17);

            // Act
            var actual = DateTimeUtilities.StartOfWeek(dt, DayOfWeek.Monday);

            // Assert
            var expected = new DateTime(2015, 05, 11);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_01()
        {
            // Arrange
            var year = 2005;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2005, 01, 03);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_02()
        {
            // Arrange
            var year = 2005;
            var week = 52;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2005, 12, 26);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_03()
        {
            // Arrange
            var year = 2006;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2006, 1, 2);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_04()
        {
            // Arrange
            var year = 2006;
            var week = 52;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2006, 12, 25);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_05()
        {
            // Arrange
            var year = 2008;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2007, 12, 31);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_06()
        {
            // Arrange
            var year = 2008;
            var week = 52;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2008, 12, 22);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_07()
        {
            // Arrange
            var year = 2009;
            var week = 1;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2008, 12, 29);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstDateOfWeekIso8601_08()
        {
            // Arrange
            var year = 2009;
            var week = 53;

            // Act
            var actual = DateTimeUtilities.FirstDateOfWeekIso8601(year, week);

            // Assert
            var expected = new DateTime(2009, 12, 28);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TruncateMilliseconds()
        {
            // Arrange
            var dt = new DateTime(2018, 2, 3, 4, 5, 6, 7, DateTimeKind.Utc);

            // Act
            var actual = DateTimeUtilities.TruncateMilliseconds(dt);

            // Assert
            var expected = new DateTime(2018, 2, 3, 4, 5, 6, 0, DateTimeKind.Utc);
            Assert.Equal(expected, actual);
        }
    }
}
