using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Meziantou.Framework.Versioning.Tests
{
    public class SemanticVersionTests
    {
        [Theory]
        [MemberData(nameof(TryParse_ShouldParseVersion_Data))]
        public void TryParse_ShouldParseVersion(string version, SemanticVersion expected)
        {
            Assert.True(SemanticVersion.TryParse(version, out var actual));
            Assert.Equal(expected, actual);
        }

        public static IEnumerable<object[]> TryParse_ShouldParseVersion_Data()
        {
            return new[]
            {
                new object[] { "1.0.0", new SemanticVersion(1, 0, 0) },
                new object[] { "v1.2.3", new SemanticVersion(1, 2, 3) },
                new object[] { "1.0.0-alpha", new SemanticVersion(1, 0, 0, "alpha") },
                new object[] { "1.0.0-alpha.1", new SemanticVersion(1, 0, 0, new[] { "alpha", "1" }, Array.Empty<string>()) },
                new object[] { "1.0.0-0123alpha", new SemanticVersion(1, 0, 0, "0123alpha") },
                new object[] { "1.1.2-alpha.1+label", new SemanticVersion(1, 1, 2, new[] { "alpha", "1" }, new[] { "label" }) },
            };
        }

        [Theory]
        [InlineData("")]
        [InlineData("1")] // Must contain 3 parameters
        [InlineData("1.2")] // Must contain 3 parameters
        [InlineData("1.2.3.4")] // Must contain 3 parameters
        [InlineData("01.2.3")] // No leading 0
        [InlineData("1.02.3")] // No leading 0
        [InlineData("1.2.03")] // No leading 0
        [InlineData("1.0.0-01")] // No leading 0
        [InlineData("1.0.0-beta.01")] // No leading 0
        [InlineData("1.0.0+é")] // Invalid character
        public void TryParse_ShouldNotParseVersion(string version)
        {
            Assert.False(SemanticVersion.TryParse(version, out _));
            Assert.Throws<ArgumentException>(() => SemanticVersion.Parse(version));
        }

        [Fact]
        public void TryParse_ShouldNotParseNullVersion()
        {
            Assert.False(SemanticVersion.TryParse(null, out _));
        }

        [Fact]
        public void Parse_ShouldNotParseNullVersion()
        {
            Assert.Throws<ArgumentNullException>(() => SemanticVersion.Parse(null));
        }

        [Theory]
        [MemberData(nameof(Operator_Data))]
        public void Operator_DifferentValues(SemanticVersion left, SemanticVersion right)
        {
            Assert.Equal(left.GetHashCode(), left.GetHashCode());
            Assert.Equal(right.GetHashCode(), right.GetHashCode());
            Assert.Equal(left, left);
            Assert.Equal(right, right);

            Assert.NotEqual(left, right);
            Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
            Assert.False(left == right);
            Assert.True(left != right);

            Assert.True(left < right);
            Assert.True(left <= right);

            Assert.False(left > right);
            Assert.False(left >= right);
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

        [Theory]
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("1.0.0+left", "1.0.0+right")]
        [InlineData("1.0.0-alpha+left", "1.0.0-alpha+right")]
        [InlineData("1.0.0-alpha.1+left", "1.0.0-alpha.1+right")]
        public void Operator_SameValues(string leftString, string rightString)
        {
            var left = SemanticVersion.Parse(leftString);
            var right = SemanticVersion.Parse(rightString);

            Assert.Equal(left.GetHashCode(), right.GetHashCode());

            Assert.Equal(left, right);
            Assert.True(left == right);
            Assert.False(left != right);

            Assert.False(left < right);
            Assert.True(left <= right);
            Assert.False(left > right);
            Assert.True(left >= right);
        }

        [Theory]
        [InlineData("1.2.3")]
        [InlineData("1.0.0+ci1")]
        [InlineData("1.0.0-alpha+label1.2")]
        [InlineData("1.0.0-alpha.1+label1")]
        [InlineData("1.0.0-alpha.1")]
        public void Test_ToString(string version)
        {
            var semanticVersion = SemanticVersion.Parse(version);
            Assert.Equal(version, semanticVersion.ToString());
        }

        [Fact]
        public void Constructor_WithInvalidPrerelease_ShouldThrowException()
        {
            Assert.Throws<ArgumentException>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: new[] { "01" }, metadata: null));
        }

        [Fact]
        public void Constructor_WithEmptyPrerelease_ShouldThrowException()
        {
            Assert.Throws<ArgumentException>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: new[] { "" }, metadata: null));
        }

        [Fact]
        public void Constructor_WithEmptyMetadata_ShouldThrowException()
        {
            Assert.Throws<ArgumentException>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: null, metadata: new[] { "" }));
        }

        [Fact]
        public void Constructor_WithInvalidMetadata_ShouldThrowException()
        {
            Assert.Throws<ArgumentException>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: null, metadata: new[] { "/" }));
        }

        [Fact]
        public void Constructor_WithInvalidMetadataString_ShouldThrowException()
        {
            Assert.Throws<ArgumentException>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: null, metadata: "label./"));
        }

        [Fact]
        public void NextPatchVersion_ShouldRemovePrereleaseTag()
        {
            var version = new SemanticVersion(1, 0, 0, "test");
            Assert.Equal(new SemanticVersion(1, 0, 0), version.NextPatchVersion());
        }

        [Fact]
        public void NextPatchVersion_ShouldIncreasePatch()
        {
            var version = new SemanticVersion(1, 0, 1);
            Assert.Equal(new SemanticVersion(1, 0, 2), version.NextPatchVersion());
        }

        [Fact]
        public void NextMinorVersion_ShouldRemovePrereleaseTag()
        {
            var version = new SemanticVersion(1, 0, 0, "test");
            Assert.Equal(new SemanticVersion(1, 0, 0), version.NextMinorVersion());
        }

        [Fact]
        public void NextMinorVersion_ShouldIncreaseMinor()
        {
            var version = new SemanticVersion(1, 0, 1);
            Assert.Equal(new SemanticVersion(1, 1, 0), version.NextMinorVersion());
        }

        [Fact]
        public void NextMajorVersion_ShouldRemovePrereleaseTag()
        {
            var version = new SemanticVersion(1, 0, 0, "test");
            Assert.Equal(new SemanticVersion(1, 0, 0), version.NextMajorVersion());
        }

        [Fact]
        public void NextMajorVersion_ShouldIncreaseMajor()
        {
            var version = new SemanticVersion(1, 2, 3);
            Assert.Equal(new SemanticVersion(2, 0, 0), version.NextMajorVersion());
        }
    }
}
