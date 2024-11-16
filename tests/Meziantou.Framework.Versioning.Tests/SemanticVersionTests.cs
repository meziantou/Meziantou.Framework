using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Versioning.Tests;

public class SemanticVersionTests
{
    [Theory]
    [MemberData(nameof(TryParse_ShouldParseVersion_Data))]
    public void TryParse_ShouldParseVersion(string version, SemanticVersion expected)
    {
        SemanticVersion.TryParse(version, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    public static TheoryData<string, SemanticVersion> TryParse_ShouldParseVersion_Data()
    {
        return new()
        {
            { "1.0.0", new SemanticVersion(1, 0, 0) },
            { "v1.2.3", new SemanticVersion(1, 2, 3) },
            { "1.0.0-alpha", new SemanticVersion(1, 0, 0, "alpha") },
            {"1.0.0-alpha.1", new SemanticVersion(1, 0, 0, ["alpha", "1"], []) },
            { "1.0.0-0123alpha", new SemanticVersion(1, 0, 0, "0123alpha") },
            { "1.1.2-alpha.1+label", new SemanticVersion(1, 1, 2, ["alpha", "1"], ["label"]) },
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
        SemanticVersion.TryParse(version, out _).Should().BeFalse();
        new Func<object>(() => SemanticVersion.Parse(version)).Should().ThrowExactly<ArgumentException>();

        SemanticVersion.TryParse(version.AsSpan(), out _).Should().BeFalse();
        new Func<object>(() => SemanticVersion.Parse(version)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void TryParse_ShouldNotParseNullVersion()
    {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
        SemanticVersion.TryParse((string)null, out _).Should().BeFalse();
#pragma warning restore IDE0004
    }

    [Fact]
    public void Parse_ShouldNotParseNullVersion()
    {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
        new Func<object>(() => SemanticVersion.Parse((string)null)).Should().ThrowExactly<ArgumentNullException>();
#pragma warning restore IDE0004
    }

    [Fact]
    public void PropertiesAreSet()
    {
        var version = SemanticVersion.Parse("1.2.3-beta.1+label");
        version.Major.Should().Be(1);
        version.Minor.Should().Be(2);
        version.Patch.Should().Be(3);
        version.IsPrerelease.Should().BeTrue();
        Assert.Equal(["beta", "1"], version.PrereleaseLabels);
        version.HasMetadata.Should().BeTrue();
        Assert.Equal(["label"], version.Metadata);
    }

    [Theory]
    [MemberData(nameof(Operator_Data))]
    public void Operator_DifferentValues(SemanticVersion left, SemanticVersion right)
    {
        left.GetHashCode().Should().Be(left.GetHashCode());
        right.GetHashCode().Should().Be(right.GetHashCode());
        left.Should().Be(left);
        right.Should().Be(right);

        right.Should().NotBe(left);
        right.GetHashCode().Should().NotBe(left.GetHashCode());
        (left == right).Should().BeFalse();
        (left != right).Should().BeTrue();

        (left < right).Should().BeTrue();
        (left <= right).Should().BeTrue();

        (left > right).Should().BeFalse();
        (left >= right).Should().BeFalse();
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

        right.GetHashCode().Should().Be(left.GetHashCode());

        right.Should().Be(left);
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();

        (left < right).Should().BeFalse();
        (left <= right).Should().BeTrue();
        (left > right).Should().BeFalse();
        (left >= right).Should().BeTrue();
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
        semanticVersion.ToString().Should().Be(version);
    }

    [Fact]
    public void Constructor_WithInvalidPrerelease_ShouldThrowException()
    {
        new Func<object>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: ["01"], metadata: null)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmptyPrerelease_ShouldThrowException()
    {
        new Func<object>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: [""], metadata: null)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmptyMetadata_ShouldThrowException()
    {
        new Func<object>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: null, metadata: [""])).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithInvalidMetadata_ShouldThrowException()
    {
        new Func<object>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: null, metadata: ["/"])).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithInvalidMetadataString_ShouldThrowException()
    {
        new Func<object>(() => new SemanticVersion(1, 2, 3, prereleaseLabel: null, metadata: "label./")).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void NextPatchVersion_ShouldRemovePrereleaseTag()
    {
        var version = new SemanticVersion(1, 0, 0, "test");
        version.NextPatchVersion().Should().Be(new SemanticVersion(1, 0, 0));
    }

    [Fact]
    public void NextPatchVersion_ShouldIncreasePatch()
    {
        var version = new SemanticVersion(1, 0, 1);
        version.NextPatchVersion().Should().Be(new SemanticVersion(1, 0, 2));
    }

    [Fact]
    public void NextMinorVersion_ShouldRemovePrereleaseTag()
    {
        var version = new SemanticVersion(1, 0, 0, "test");
        version.NextMinorVersion().Should().Be(new SemanticVersion(1, 0, 0));
    }

    [Fact]
    public void NextMinorVersion_ShouldIncreaseMinor()
    {
        var version = new SemanticVersion(1, 0, 1);
        version.NextMinorVersion().Should().Be(new SemanticVersion(1, 1, 0));
    }

    [Fact]
    public void NextMajorVersion_ShouldRemovePrereleaseTag()
    {
        var version = new SemanticVersion(1, 0, 0, "test");
        version.NextMajorVersion().Should().Be(new SemanticVersion(1, 0, 0));
    }

    [Fact]
    public void NextMajorVersion_ShouldIncreaseMajor()
    {
        var version = new SemanticVersion(1, 2, 3);
        version.NextMajorVersion().Should().Be(new SemanticVersion(2, 0, 0));
    }
}
