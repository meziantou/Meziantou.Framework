using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace TestUtilities;

public sealed class Dummy
{
    private readonly ITestOutputHelper _outputHelper;

    public Dummy(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1004:Test methods should not be skipped", Justification = "<Pending>")]
    [Fact(Skip = "For testing purpose")]
    public void Test()
    {
        _outputHelper.WriteLine("This test output one line");
        false.Should().BeTrue("This is a dummy test");
    }
}
