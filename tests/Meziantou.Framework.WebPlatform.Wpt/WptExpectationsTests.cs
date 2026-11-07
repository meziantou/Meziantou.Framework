using Meziantou.Framework.WebPlatform.Wpt.Runner;

namespace Meziantou.Framework.WebPlatform.Wpt;

public sealed class WptExpectationsTests
{
    /// <summary>
    /// An update of the expectations (<c>WPT_UPDATE_EXPECTATIONS=1</c>) whose process is killed or canceled before the end
    /// only updates the expectations of the tests that ran: this test fails until the update is run again (or its marker is
    /// deleted), so that a partial update does not go unnoticed.
    /// </summary>
    [Fact]
    public void UpdatesAreComplete()
    {
        var interrupted = WptExpectations.GetInterruptedUpdates();
        Assert.True(interrupted.Count == 0, string.Join(Environment.NewLine, interrupted));
    }
}
