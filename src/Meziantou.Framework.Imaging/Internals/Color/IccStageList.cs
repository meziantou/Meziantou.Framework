namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The stages of a conversion under construction. Consecutive matrix stages are combined into one.</summary>
internal sealed class IccStageList
{
    private readonly List<IccStage> _stages = [];

    public void Add(IccStage stage)
    {
        if (stage is IccMatrixStage matrix && _stages.Count > 0 && _stages[^1] is IccMatrixStage previous)
        {
            _stages[^1] = previous.Then(matrix);
            return;
        }

        _stages.Add(stage);
    }

    public IccStage[] ToArray() => [.. _stages];
}
