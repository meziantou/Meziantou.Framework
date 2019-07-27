using System.Collections.Generic;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class ShortNameUtilitiesTests
    {
        [Fact]
        public void CreateShortName_01()
        {
            // Arrange
            var name = "bbb";
            var names = new List<string> { "aaa", "aab" };

            // Act
            var shortName = ShortName.Create(names, 3, name);

            // Assert
            Assert.Equal("bbb", shortName);
        }

        [Fact]
        public void CreateShortName_02()
        {
            // Arrange
            var name = "aaa";
            var names = new List<string> { "aaa", "aab" };

            // Act
            var shortName = ShortName.Create(names, 3, name);

            // Assert
            Assert.Equal("aa0", shortName);
        }

        [Fact]
        public void BuildShortNames_01()
        {
            // Arrange
            var names = new List<string> { "aaaa", "aaab", "aaa", "aab", "other" };

            // Act
            var shortNames = ShortName.Create(names, 3);

            // Assert
            Assert.Equal("aa0", shortNames["aaaa"]);
            Assert.Equal("aa1", shortNames["aaab"]);
            Assert.Equal("aaa", shortNames["aaa"]);
            Assert.Equal("aab", shortNames["aab"]);
            Assert.Equal("oth", shortNames["other"]);
        }
    }
}
