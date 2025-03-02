namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

public sealed class BoundAndQuery : BoundQuery
{
    internal BoundAndQuery(BoundQuery left, BoundQuery right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public BoundQuery Left { get; }

    public BoundQuery Right { get; }
}
