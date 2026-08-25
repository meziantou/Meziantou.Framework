namespace Meziantou.Framework.Http.Caching;

/// <summary>A Structured Fields bare item.</summary>
internal readonly struct StructuredFieldItem
{
    private StructuredFieldItem(StructuredFieldItemType type, bool booleanValue, string? stringValue)
    {
        Type = type;
        BooleanValue = booleanValue;
        StringValue = stringValue;
    }

    public static StructuredFieldItem True { get; } = new(StructuredFieldItemType.Boolean, booleanValue: true, stringValue: null);
    public static StructuredFieldItem False { get; } = new(StructuredFieldItemType.Boolean, booleanValue: false, stringValue: null);

    public static StructuredFieldItem FromString(string value) => new(StructuredFieldItemType.String, booleanValue: false, value);

    public StructuredFieldItemType Type { get; }
    public bool BooleanValue { get; }
    public string? StringValue { get; }
}
