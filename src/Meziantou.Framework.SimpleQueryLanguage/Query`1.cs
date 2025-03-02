namespace Meziantou.Framework.SimpleQueryLanguage;

public sealed class Query<T>
{
    private readonly Predicate<T> _predicate;

    public string Text { get; }

    internal Query(string text, Predicate<T> predicate)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool Evaluate(T value)
    {
        return _predicate(value);
    }
}
