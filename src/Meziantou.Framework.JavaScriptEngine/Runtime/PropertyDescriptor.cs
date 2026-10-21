namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A property descriptor (https://tc39.es/ecma262/#sec-property-descriptor-specification-type); its fields may be absent.</summary>
internal struct PropertyDescriptor
{
    public JsValue Value;
    public JsValue Get;
    public JsValue Set;
    public PropertyFlags Flags;
    public DescriptorFields Fields;

    public readonly bool IsAccessorDescriptor => (Fields & DescriptorFields.Accessor) != 0;

    public readonly bool IsDataDescriptor => (Fields & DescriptorFields.Data) != 0;

    public readonly bool IsGenericDescriptor => !IsAccessorDescriptor && !IsDataDescriptor;

    public readonly bool HasValue => (Fields & DescriptorFields.Value) != 0;

    public readonly bool HasWritable => (Fields & DescriptorFields.Writable) != 0;

    public readonly bool HasGet => (Fields & DescriptorFields.Get) != 0;

    public readonly bool HasSet => (Fields & DescriptorFields.Set) != 0;

    public readonly bool HasEnumerable => (Fields & DescriptorFields.Enumerable) != 0;

    public readonly bool HasConfigurable => (Fields & DescriptorFields.Configurable) != 0;

    public readonly bool Writable => (Flags & PropertyFlags.Writable) != 0;

    public readonly bool Enumerable => (Flags & PropertyFlags.Enumerable) != 0;

    public readonly bool Configurable => (Flags & PropertyFlags.Configurable) != 0;

    /// <summary>Creates a complete data descriptor.</summary>
    public static PropertyDescriptor Data(JsValue value, PropertyFlags flags = PropertyFlags.Default)
    {
        return new PropertyDescriptor { Value = value, Flags = flags & PropertyFlags.Default, Fields = DescriptorFields.All };
    }

    /// <summary>Creates a complete accessor descriptor.</summary>
    public static PropertyDescriptor Accessor(JsValue getter, JsValue setter, PropertyFlags flags = PropertyFlags.Configurable)
    {
        return new PropertyDescriptor { Get = getter, Set = setter, Flags = flags & (PropertyFlags.Enumerable | PropertyFlags.Configurable), Fields = DescriptorFields.Accessor | DescriptorFields.Enumerable | DescriptorFields.Configurable };
    }

    /// <summary>A descriptor with only [[Value]] (the descriptor of an assignment to an existing data property).</summary>
    public static PropertyDescriptor ValueOnly(JsValue value) => new() { Value = value, Fields = DescriptorFields.Value };
}
