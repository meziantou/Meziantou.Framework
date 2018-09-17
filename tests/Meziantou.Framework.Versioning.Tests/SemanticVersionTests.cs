using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Versioning.Tests
{
    [TestClass]
    public class SemanticVersionTests
    {
        [DataTestMethod]
        [DynamicData(nameof(TryParse_ShouldParseVersion_Data), DynamicDataSourceType.Method)]
        public void TryParse_ShouldParseVersion(string version, SemanticVersion expected)
        {
            Assert.IsTrue(SemanticVersion.TryParse(version, out var actual));
            Assert.AreEqual(expected, actual);
        }

        public static IEnumerable<object[]> TryParse_ShouldParseVersion_Data()
        {
            return new[]
            {
                new object[] { "1.0.0", new SemanticVersion(1, 0, 0) },
                new object[] { "v1.2.3", new SemanticVersion(1, 2, 3) },
                new object[] { "1.0.0-alpha", new SemanticVersion(1, 0, 0, "alpha") },
                new object[] { "1.0.0-alpha.1", new SemanticVersion(1, 0, 0, new[] { "alpha", "1" }, new string[0]) },
                new object[] { "1.0.0-0123alpha", new SemanticVersion(1, 0, 0, "0123alpha") },
                new object[] { "1.1.2-alpha.1+label", new SemanticVersion(1, 1, 2, new[] { "alpha", "1" }, new[] { "label" }) },
            };
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("1")] // Must contain 3 parameters
        [DataRow("1.2")] // Must contain 3 parameters
        [DataRow("1.2.3.4")] // Must contain 3 parameters
        [DataRow("01.2.3")] // No leading 0
        [DataRow("1.02.3")] // No leading 0
        [DataRow("1.2.03")] // No leading 0
        [DataRow("1.0.0-01")] // No leading 0
        [DataRow("1.0.0-beta.01")] // No leading 0
        [DataRow("1.0.0+é")] // Invalid character
        public void TryParse_ShouldNotParseVersion(string version)
        {
            Assert.IsFalse(SemanticVersion.TryParse(version, out _));
            Assert.ThrowsException<ArgumentException>(() => SemanticVersion.Parse(version));
        }

        [TestMethod]
        public void TryParse_ShouldNotParseNullVersion()
        {
            Assert.IsFalse(SemanticVersion.TryParse(null, out _));
        }

        [TestMethod]
        public void Parse_ShouldNotParseNullVersion()
        {
            Assert.ThrowsException<ArgumentNullException>(() => SemanticVersion.Parse(null));
        }

        [DataTestMethod]
        [DynamicData(nameof(Operator_Data), DynamicDataSourceType.Method)]
        public void Operator_DifferentValues(SemanticVersion left, SemanticVersion right)
        {
            Assert.AreEqual(left.GetHashCode(), left.GetHashCode());
            Assert.AreEqual(right.GetHashCode(), right.GetHashCode());
            Assert.AreEqual(left, left);
            Assert.AreEqual(right, right);

            Assert.AreNotEqual(left, right);
            Assert.AreNotEqual(left.GetHashCode(), right.GetHashCode());
            Assert.IsFalse(left == right);
            Assert.IsTrue(left != right);

            Assert.IsTrue(left < right);
            Assert.IsTrue(left <= right);

            Assert.IsFalse(left > right);
            Assert.IsFalse(left >= right);
        }

        public static IEnumerable<object[]> Operator_Data()
        {
            var orderedVersions = new[]
            {
                new SemanticVersion(1, 0, 0, "alpha"),
                new SemanticVersion(1, 0, 0, "alpha.1"),
                new SemanticVersion(1, 0, 0, "alpha.beta"),
                new SemanticVersion(1, 0, 0, "beta"),
                new SemanticVersion(1, 0, 0, "beta.2"),
                new SemanticVersion(1, 0, 0, "beta.11"),
                new SemanticVersion(1, 0, 0, "beta.rc.1"),
                new SemanticVersion(1, 0, 0),
            };

            var left = orderedVersions.Take(orderedVersions.Length - 1);
            var right = orderedVersions.Skip(1);

            return left.Zip(right, (l, r) => new[] { l, r });
        }

        [DataTestMethod]
        [DataRow("1.2.3", "1.2.3")]
        [DataRow("1.0.0+left", "1.0.0+right")]
        [DataRow("1.0.0-alpha+left", "1.0.0-alpha+right")]
        [DataRow("1.0.0-alpha.1+left", "1.0.0-alpha.1+right")]
        public void Operator_SameValues(string leftString, string rightString)
        {
            var left = SemanticVersion.Parse(leftString);
            var right = SemanticVersion.Parse(rightString);

            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());

            Assert.AreEqual(left, right);
            Assert.IsTrue(left == right);
            Assert.IsFalse(left != right);

            Assert.IsFalse(left < right);
            Assert.IsTrue(left <= right);
            Assert.IsFalse(left > right);
            Assert.IsTrue(left >= right);
        }

        [DataTestMethod]
        [DataRow("1.2.3")]
        [DataRow("1.0.0+ci1")]
        [DataRow("1.0.0-alpha+label1.2")]
        [DataRow("1.0.0-alpha.1+label1")]
        [DataRow("1.0.0-alpha.1")]
        public void ToString(string version)
        {
            var semanticVersion = SemanticVersion.Parse(version);
            Assert.AreEqual(version, semanticVersion.ToString());
        }

        [TestMethod]
        public void Constructor_WithInvalidPrerelease_ShouldThrowException()
        {
            Assert.ThrowsException<ArgumentException>(() => new SemanticVersion(1, 2, 3, new[] { "01" }, null));
        }

        [TestMethod]
        public void Constructor_WithEmptyPrerelease_ShouldThrowException()
        {
            Assert.ThrowsException<ArgumentException>(() => new SemanticVersion(1, 2, 3, new[] { "" }, null));
        }

        [TestMethod]
        public void Constructor_WithEmptyMetadata_ShouldThrowException()
        {
            Assert.ThrowsException<ArgumentException>(() => new SemanticVersion(1, 2, 3, null, new[] { "" }));
        }

        [TestMethod]
        public void Constructor_WithInvalidMetadata_ShouldThrowException()
        {
            Assert.ThrowsException<ArgumentException>(() => new SemanticVersion(1, 2, 3, null, new[] { "/" }));
        }

        [TestMethod]
        public void Constructor_WithInvalidMetadataString_ShouldThrowException()
        {
            Assert.ThrowsException<ArgumentException>(() => new SemanticVersion(1, 2, 3, null, "label./"));
        }

        [TestMethod]
        public void NextPatchVersion_ShouldRemovePrereleaseTag()
        {
            var version = new SemanticVersion(1, 0, 0, "test");
            Assert.AreEqual(new SemanticVersion(1, 0, 0), version.NextPatchVersion());
        }

        [TestMethod]
        public void NextPatchVersion_ShouldIncreasePatch()
        {
            var version = new SemanticVersion(1, 0, 1);
            Assert.AreEqual(new SemanticVersion(1, 0, 2), version.NextPatchVersion());
        }

        [TestMethod]
        public void NextMinorVersion_ShouldRemovePrereleaseTag()
        {
            var version = new SemanticVersion(1, 0, 0, "test");
            Assert.AreEqual(new SemanticVersion(1, 0, 0), version.NextMinorVersion());
        }

        [TestMethod]
        public void NextMinorVersion_ShouldIncreaseMinor()
        {
            var version = new SemanticVersion(1, 0, 1);
            Assert.AreEqual(new SemanticVersion(1, 1, 0), version.NextMinorVersion());
        }

        [TestMethod]
        public void NextMajorVersion_ShouldRemovePrereleaseTag()
        {
            var version = new SemanticVersion(1, 0, 0, "test");
            Assert.AreEqual(new SemanticVersion(1, 0, 0), version.NextMajorVersion());
        }

        [TestMethod]
        public void NextMajorVersion_ShouldIncreaseMajor()
        {
            var version = new SemanticVersion(1, 2, 3);
            Assert.AreEqual(new SemanticVersion(2, 0, 0), version.NextMajorVersion());
        }
    }
}
