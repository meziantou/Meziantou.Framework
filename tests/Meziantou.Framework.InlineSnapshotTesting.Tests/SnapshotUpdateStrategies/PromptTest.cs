using FluentAssertions;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests.SnapshotUpdateStrategies;
public sealed class PromptTest
{
    [Fact]
    public void ApplyToAllFiles()
    {
        var prompt = new PromptMock(
                    ctx => new PromptResult(PromptConfigurationMode.Disallow, TimeSpan.FromHours(1), ApplyToAllFiles: true));
        var instance = new PromptStrategy(prompt) { FilePath = Path.GetTempFileName() };

        instance.CanUpdateSnapshot(InlineSnapshotSettings.Default, "file1.cs").Should().BeFalse();
        instance.CanUpdateSnapshot(InlineSnapshotSettings.Default, "file2.cs").Should().BeFalse();
        prompt.AssertAllCalled();
    }

    [Fact]
    public void DifferentPerFile()
    {
        var prompt = new PromptMock(
                    ctx => new PromptResult(PromptConfigurationMode.Disallow, TimeSpan.FromHours(1), ApplyToAllFiles: false),
                    ctx => new PromptResult(PromptConfigurationMode.OverwriteWithoutFailure, TimeSpan.FromHours(1), ApplyToAllFiles: false));
        var instance = new PromptStrategy(prompt) { FilePath = Path.GetTempFileName() };

        instance.CanUpdateSnapshot(InlineSnapshotSettings.Default, "file1.cs").Should().BeFalse();
        instance.CanUpdateSnapshot(InlineSnapshotSettings.Default, "file2.cs").Should().BeTrue();
        prompt.AssertAllCalled();
    }
    
    [Fact]
    public void Expires_AskAgain()
    {
        // Use negative period to be sure the entry is not reused
        var prompt = new PromptMock(
                    ctx => new PromptResult(PromptConfigurationMode.Disallow, TimeSpan.FromHours(-1), ApplyToAllFiles: false),
                    ctx => new PromptResult(PromptConfigurationMode.Overwrite, TimeSpan.FromHours(-1), ApplyToAllFiles: false));
        var instance = new PromptStrategy(prompt) { FilePath = Path.GetTempFileName() };

        instance.CanUpdateSnapshot(InlineSnapshotSettings.Default, "file.cs").Should().BeFalse();
        instance.CanUpdateSnapshot(InlineSnapshotSettings.Default, "file.cs").Should().BeTrue();
        prompt.AssertAllCalled();
    }

    private sealed class PromptMock : Prompt
    {
        private readonly Queue<Func<PromptContext, PromptResult>> _results;

        public PromptMock(params Func<PromptContext, PromptResult>[] results)
        {
            _results = new Queue<Func<PromptContext, PromptResult>>(results);
        }

        public override PromptResult Ask(PromptContext context)
        {
            return _results.Dequeue()(context);
        }

        public void AssertAllCalled()
        {
            _results.Should().BeEmpty();
        }
    }
}
