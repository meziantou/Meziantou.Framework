namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

/// <summary>Represents a bound free-text query.</summary>
public sealed class BoundTextQuery : BoundQuery
{
    internal BoundTextQuery(bool isNegated, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IsNegated = isNegated;
        Text = text;
    }

    /// <summary>Gets a value indicating whether the query is negated.</summary>
    public bool IsNegated { get; }

    /// <summary>Gets the text to search for.</summary>
    public string Text { get; }
}
