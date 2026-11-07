namespace Meziantou.Framework.Http.Caching;

/// <summary>The value of a Structured Fields dictionary member: either a single item or an inner list.</summary>
internal readonly struct StructuredFieldValue
{
    private readonly List<StructuredFieldItem>? _innerList;
    private readonly StructuredFieldItem _item;

    private StructuredFieldValue(StructuredFieldItem item, List<StructuredFieldItem>? innerList)
    {
        _item = item;
        _innerList = innerList;
    }

    public static StructuredFieldValue FromItem(StructuredFieldItem item) => new(item, innerList: null);

    public static StructuredFieldValue FromInnerList(List<StructuredFieldItem> items) => new(default, items);

    public bool IsInnerList => _innerList is not null;

    public StructuredFieldItem Item => _item;

    public IReadOnlyList<StructuredFieldItem> InnerList => _innerList ?? [];
}
