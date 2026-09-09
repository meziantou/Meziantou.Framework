namespace Meziantou.Framework.Language.Tests;

public sealed class DiagnosticDescriptorTests
{
    [Fact]
    public void Constructor_RoundTripsEveryProperty()
    {
        var descriptor = new DiagnosticDescriptor("XML0001", "Missing end tag", "Missing end tag for '{0}'.", DiagnosticSeverity.Error);

        Assert.Equal("XML0001", descriptor.Id);
        Assert.Equal("Missing end tag", descriptor.Title);
        Assert.Equal("Missing end tag for '{0}'.", descriptor.MessageFormat);
        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_RejectsABlankIdOrTitle(string value)
    {
        Assert.Equal("id", Assert.Throws<ArgumentException>(() => new DiagnosticDescriptor(value, "title", "format", DiagnosticSeverity.Error)).ParamName);
        Assert.Equal("title", Assert.Throws<ArgumentException>(() => new DiagnosticDescriptor("ID0001", value, "format", DiagnosticSeverity.Error)).ParamName);
    }

    [Fact]
    public void Constructor_RejectsANullIdOrTitle()
    {
        Assert.Equal("id", Assert.Throws<ArgumentNullException>(() => new DiagnosticDescriptor(null!, "title", "format", DiagnosticSeverity.Error)).ParamName);
        Assert.Equal("title", Assert.Throws<ArgumentNullException>(() => new DiagnosticDescriptor("ID0001", null!, "format", DiagnosticSeverity.Error)).ParamName);
    }

    [Fact]
    public void Constructor_RejectsANullMessageFormat()
    {
        Assert.Equal("messageFormat", Assert.Throws<ArgumentNullException>(() => new DiagnosticDescriptor("ID0001", "title", null!, DiagnosticSeverity.Error)).ParamName);
    }
}
