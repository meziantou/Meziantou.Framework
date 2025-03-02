namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal abstract class Prompt
{
    public virtual void OnSnapshotChanged()
    {
    }

    public abstract PromptResult Ask(PromptContext context);
}
