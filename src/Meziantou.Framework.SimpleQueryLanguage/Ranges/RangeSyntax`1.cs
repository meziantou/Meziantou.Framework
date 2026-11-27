namespace Meziantou.Framework.SimpleQueryLanguage.Ranges;

/// <summary>Represents a range expression that can evaluate whether a value falls within the range.</summary>
/// <typeparam name="T">The type of values in the range.</typeparam>
public abstract class RangeSyntax<T>
{
    private protected RangeSyntax()
    {
    }

    /// <summary>Determines whether a value is within the range using the default comparer.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true"/> if the value is within the range; otherwise, <see langword="false"/>.</returns>
    public bool IsInRange(T value)
    {
        return IsInRange(value, comparer: null);
    }

    /// <summary>Determines whether a value is within the range using a custom comparer.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="comparer">The comparer to use for comparison, or null to use the default comparer.</param>
    /// <returns><see langword="true"/> if the value is within the range; otherwise, <see langword="false"/>.</returns>
    public abstract bool IsInRange(T value, IComparer<T>? comparer);

    /// <summary>Compares with the comparer, or with the default comparer, called directly so the JIT can devirtualize and inline it.</summary>
    private protected static int Compare(T x, T y, IComparer<T>? comparer)
    {
        return comparer is null ? Comparer<T>.Default.Compare(x, y) : comparer.Compare(x, y);
    }
}
