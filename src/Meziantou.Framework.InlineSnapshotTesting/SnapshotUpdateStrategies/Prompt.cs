namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal abstract class Prompt
{
    public abstract PromptResult Ask(PromptContext context);
}
