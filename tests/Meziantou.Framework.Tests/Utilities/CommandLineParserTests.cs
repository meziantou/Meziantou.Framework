using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class CommandLineParserTests
    {
        [TestMethod]
        public void HasArgument_01()
        {
            // Arrange
            var args = new[] { "/a", "/b=value2" };
            var parser = new CommandLineParser();
            parser.Parse(args);

            // Act
            var valueA = parser.HasArgument("a");
            var valueB = parser.HasArgument("b");
            var valueC = parser.HasArgument("c");

            // Assert
            Assert.IsTrue(valueA);
            Assert.IsTrue(valueB);
            Assert.IsFalse(valueC);
        }

        [TestMethod]
        public void GetArgument_01()
        {
            // Arrange
            var args = new[] { "/a=value1", "/b=value2" };
            var parser = new CommandLineParser();
            parser.Parse(args);

            // Act
            var valueA = parser.GetArgument("a");
            var valueB = parser.GetArgument("b");
            var helpRequested = parser.HelpRequested;

            // Assert
            Assert.AreEqual("value1", valueA);
            Assert.AreEqual("value2", valueB);
            Assert.IsFalse(helpRequested);
        }

        [TestMethod]
        public void GetArgument_02()
        {
            // Arrange
            var args = new[] { "/a=value1", "value2" };
            var parser = new CommandLineParser();
            parser.Parse(args);

            // Act
            var valueA = parser.GetArgument("a");
            var valueB = parser.GetArgument(1);
            var helpRequested = parser.HelpRequested;

            // Assert
            Assert.AreEqual("value1", valueA);
            Assert.AreEqual("value2", valueB);
            Assert.IsFalse(helpRequested);
        }

        [TestMethod]
        public void HelpRequested_01()
        {
            // Arrange
            var args = new[] { "/?" };
            var parser = new CommandLineParser();
            parser.Parse(args);

            // Act
            var helpRequested = parser.HelpRequested;

            // Assert
            Assert.IsTrue(helpRequested);
        }

        [TestMethod]
        public void HelpRequested_02()
        {
            // Arrange
            var args = new[] { "test", "/help" };
            var parser = new CommandLineParser();
            parser.Parse(args);

            // Act
            var helpRequested = parser.HelpRequested;

            // Assert
            Assert.IsTrue(helpRequested);
        }

        [TestMethod]
        public void HelpRequested_03()
        {
            // Arrange
            var args = new[] { "test", "test" };
            var parser = new CommandLineParser();
            parser.Parse(args);

            // Act
            var helpRequested = parser.HelpRequested;

            // Assert
            Assert.IsFalse(helpRequested);
        }
    }
}
