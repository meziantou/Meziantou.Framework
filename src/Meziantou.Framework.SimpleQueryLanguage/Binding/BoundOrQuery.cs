namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

public sealed class BoundOrQuery : BoundQuery
{
    public BoundOrQuery(BoundQuery left, BoundQuery right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
    }

    public BoundQuery Left { get; }

    public BoundQuery Right { get; }
}
