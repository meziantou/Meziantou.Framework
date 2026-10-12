namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>An axis of a variable font: its tag, the range of its values and its default, in the units of the axis.</summary>
internal sealed record FontAxisDefinition(string Tag, double Minimum, double Default, double Maximum, string Name, bool IsHidden = false)
{
    /// <summary>Gets the pairs (from, to) of the <c>avar</c> table for the axis, on normalized values; empty for no mapping.</summary>
    public IReadOnlyList<(double From, double To)> Map { get; init; } = [];
}
