namespace Meziantou.Framework.Language.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void Equality_ComparesTheLocationByValue()
    {
        var location = new Location(new TextSpan(1, 2), SourceText.From("abc"));
        var diagnostic = new Diagnostic("ID0001", "message", DiagnosticSeverity.Error, location);

        // Location is a class, so record equality only works because it overrides Equals.
        Assert.Equal(new Diagnostic("ID0001", "message", DiagnosticSeverity.Error, new Location(new TextSpan(1, 2), location.SourceText)), diagnostic);
        Assert.NotEqual(new Diagnostic("ID0001", "message", DiagnosticSeverity.Error, new Location(new TextSpan(2, 2), location.SourceText)), diagnostic);
    }

    [Fact]
    public void Deconstruct_YieldsEveryComponent()
    {
        var location = Location.None;
        var (id, message, severity, deconstructedLocation) = new Diagnostic("ID0001", "message", DiagnosticSeverity.Warning, location);

        Assert.Equal("ID0001", id);
        Assert.Equal("message", message);
        Assert.Equal(DiagnosticSeverity.Warning, severity);
        Assert.Same(location, deconstructedLocation);
    }

    [Fact]
    public void Severity_OrdersFromHiddenToError()
    {
        // Pinned because callers filter on `severity >= DiagnosticSeverity.Warning`, which reordering would break.
        Assert.Equal(0, (int)DiagnosticSeverity.Hidden);
        Assert.Equal(1, (int)DiagnosticSeverity.Info);
        Assert.Equal(2, (int)DiagnosticSeverity.Warning);
        Assert.Equal(3, (int)DiagnosticSeverity.Error);
    }
}
