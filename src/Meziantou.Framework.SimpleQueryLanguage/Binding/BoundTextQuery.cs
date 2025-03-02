namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

public sealed class BoundTextQuery : BoundQuery
{
    internal BoundTextQuery(bool isNegated, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IsNegated = isNegated;
        Text = text;
    }

    public bool IsNegated { get; }

    public string Text { get; }
}
