namespace Meziantou.Framework.Assertions;

/// <summary>Configures assertion failure formatting.</summary>
public sealed class FormatterOptions
{
    /// <summary>Gets or sets the number of items to format from the start of an enumerable before truncating it.</summary>
    /// <remarks>
    /// When there is no highlighted item, or when the highlighted item is within this leading range, the formatter writes items from the beginning of the enumerable.
    /// If <see cref="SuffixItemCount"/> requires more items after a highlighted item, the formatter can write more than this value.
    /// When the highlighted item index is greater than or equal to this value, the formatter switches to focused mode: it writes <see cref="PrefixItemCount"/> items from the beginning, an ellipsis, and a window around the highlighted item controlled by <see cref="HighlightedContextItemCount"/>.
    /// </remarks>
    public int MaxFormattedItems
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 20;

    /// <summary>Gets or sets the number of items to keep from the start of an enumerable when a highlighted item is outside the leading range.</summary>
    /// <remarks>
    /// This value is used only in focused mode, when the highlighted item index is greater than or equal to <see cref="MaxFormattedItems"/>.
    /// It preserves the beginning of the enumerable before the ellipsis and the highlighted-item context window.
    /// </remarks>
    public int PrefixItemCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 6;

    /// <summary>Gets or sets the minimum number of items to format after a highlighted item in the leading range.</summary>
    /// <remarks>
    /// This value is used when the highlighted item index is less than <see cref="MaxFormattedItems"/>.
    /// In that case, the formatter writes at least <see cref="MaxFormattedItems"/> items, and can continue up to the highlighted item plus this many following items.
    /// This lets assertion failures found near the beginning of a snapshot show extra items after the difference.
    /// </remarks>
    public int SuffixItemCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets or sets the number of neighboring items to format before and after a highlighted item in focused mode.</summary>
    /// <remarks>
    /// This value is used only when the highlighted item index is greater than or equal to <see cref="MaxFormattedItems"/>.
    /// The formatter then writes <see cref="PrefixItemCount"/> items from the beginning, an ellipsis when items were skipped, and up to this many items on each side of the highlighted item.
    /// </remarks>
    public int HighlightedContextItemCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 4;
}
