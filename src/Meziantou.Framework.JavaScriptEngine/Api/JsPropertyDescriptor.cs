namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The fields and attributes of a <see cref="JsPropertyDescriptor"/>.</summary>
[Flags]
public enum JsPropertyDescriptorFlags
{
    /// <summary>No field.</summary>
    None = 0,

    /// <summary>The [[Value]] field is present.</summary>
    HasValue = 1,

    /// <summary>The [[Get]] field is present.</summary>
    HasGet = 2,

    /// <summary>The [[Set]] field is present.</summary>
    HasSet = 4,

    /// <summary>The [[Writable]] field is present.</summary>
    HasWritable = 8,

    /// <summary>The [[Enumerable]] field is present.</summary>
    HasEnumerable = 16,

    /// <summary>The [[Configurable]] field is present.</summary>
    HasConfigurable = 32,

    /// <summary>The value of the [[Writable]] field.</summary>
    Writable = 64,

    /// <summary>The value of the [[Enumerable]] field.</summary>
    Enumerable = 128,

    /// <summary>The value of the [[Configurable]] field.</summary>
    Configurable = 256,
}

/// <summary>A property descriptor (https://tc39.es/ecma262/#sec-property-descriptor-specification-type): complete, or partial (the argument of <c>Object.defineProperty</c>).</summary>
public readonly struct JsPropertyDescriptor
{
    /// <summary>Creates a descriptor from its fields.</summary>
    public JsPropertyDescriptor(JsValue value, JsValue getter, JsValue setter, JsPropertyDescriptorFlags flags)
    {
        Value = value;
        Getter = getter;
        Setter = setter;
        Flags = flags;
    }

    /// <summary>Gets the [[Value]] field (undefined when absent).</summary>
    public JsValue Value { get; }

    /// <summary>Gets the [[Get]] field (undefined when absent).</summary>
    public JsValue Getter { get; }

    /// <summary>Gets the [[Set]] field (undefined when absent).</summary>
    public JsValue Setter { get; }

    /// <summary>Gets the fields and the attributes.</summary>
    public JsPropertyDescriptorFlags Flags { get; }

    /// <summary>Gets whether the descriptor is a data descriptor.</summary>
    public bool IsDataDescriptor => (Flags & (JsPropertyDescriptorFlags.HasValue | JsPropertyDescriptorFlags.HasWritable)) != 0;

    /// <summary>Gets whether the descriptor is an accessor descriptor.</summary>
    public bool IsAccessorDescriptor => (Flags & (JsPropertyDescriptorFlags.HasGet | JsPropertyDescriptorFlags.HasSet)) != 0;

    /// <summary>Gets the [[Writable]] attribute.</summary>
    public bool Writable => (Flags & JsPropertyDescriptorFlags.Writable) != 0;

    /// <summary>Gets the [[Enumerable]] attribute.</summary>
    public bool Enumerable => (Flags & JsPropertyDescriptorFlags.Enumerable) != 0;

    /// <summary>Gets the [[Configurable]] attribute.</summary>
    public bool Configurable => (Flags & JsPropertyDescriptorFlags.Configurable) != 0;

    /// <summary>Creates a complete data descriptor.</summary>
    public static JsPropertyDescriptor Data(JsValue value, bool writable = true, bool enumerable = true, bool configurable = true)
        => new(value, default, default, JsPropertyDescriptorFlags.HasValue | JsPropertyDescriptorFlags.HasWritable | JsPropertyDescriptorFlags.HasEnumerable | JsPropertyDescriptorFlags.HasConfigurable
            | (writable ? JsPropertyDescriptorFlags.Writable : 0) | (enumerable ? JsPropertyDescriptorFlags.Enumerable : 0) | (configurable ? JsPropertyDescriptorFlags.Configurable : 0));

    /// <summary>Creates a complete accessor descriptor; the getter and the setter are functions or undefined.</summary>
    public static JsPropertyDescriptor Accessor(JsValue getter, JsValue setter, bool enumerable = false, bool configurable = true)
        => new(default, getter, setter, JsPropertyDescriptorFlags.HasGet | JsPropertyDescriptorFlags.HasSet | JsPropertyDescriptorFlags.HasEnumerable | JsPropertyDescriptorFlags.HasConfigurable
            | (enumerable ? JsPropertyDescriptorFlags.Enumerable : 0) | (configurable ? JsPropertyDescriptorFlags.Configurable : 0));

    internal static JsPropertyDescriptor FromInternal(in PropertyDescriptor descriptor)
    {
        var fields = descriptor.Fields;
        var flags = JsPropertyDescriptorFlags.None;
        if ((fields & DescriptorFields.Value) != 0)
        {
            flags |= JsPropertyDescriptorFlags.HasValue;
        }

        if ((fields & DescriptorFields.Get) != 0)
        {
            flags |= JsPropertyDescriptorFlags.HasGet;
        }

        if ((fields & DescriptorFields.Set) != 0)
        {
            flags |= JsPropertyDescriptorFlags.HasSet;
        }

        if ((fields & DescriptorFields.Writable) != 0)
        {
            flags |= JsPropertyDescriptorFlags.HasWritable;
        }

        if ((fields & DescriptorFields.Enumerable) != 0)
        {
            flags |= JsPropertyDescriptorFlags.HasEnumerable;
        }

        if ((fields & DescriptorFields.Configurable) != 0)
        {
            flags |= JsPropertyDescriptorFlags.HasConfigurable;
        }

        if (descriptor.Writable)
        {
            flags |= JsPropertyDescriptorFlags.Writable;
        }

        if (descriptor.Enumerable)
        {
            flags |= JsPropertyDescriptorFlags.Enumerable;
        }

        if (descriptor.Configurable)
        {
            flags |= JsPropertyDescriptorFlags.Configurable;
        }

        return new JsPropertyDescriptor(descriptor.Value, descriptor.Get, descriptor.Set, flags);
    }

    internal PropertyDescriptor ToInternal()
    {
        var fields = DescriptorFields.None;
        var attributes = PropertyFlags.None;
        if ((Flags & JsPropertyDescriptorFlags.HasValue) != 0)
        {
            fields |= DescriptorFields.Value;
        }

        if ((Flags & JsPropertyDescriptorFlags.HasGet) != 0)
        {
            fields |= DescriptorFields.Get;
        }

        if ((Flags & JsPropertyDescriptorFlags.HasSet) != 0)
        {
            fields |= DescriptorFields.Set;
        }

        if ((Flags & JsPropertyDescriptorFlags.HasWritable) != 0)
        {
            fields |= DescriptorFields.Writable;
        }

        if ((Flags & JsPropertyDescriptorFlags.HasEnumerable) != 0)
        {
            fields |= DescriptorFields.Enumerable;
        }

        if ((Flags & JsPropertyDescriptorFlags.HasConfigurable) != 0)
        {
            fields |= DescriptorFields.Configurable;
        }

        if (Writable)
        {
            attributes |= PropertyFlags.Writable;
        }

        if (Enumerable)
        {
            attributes |= PropertyFlags.Enumerable;
        }

        if (Configurable)
        {
            attributes |= PropertyFlags.Configurable;
        }

        return new PropertyDescriptor { Value = Value, Get = Getter, Set = Setter, Flags = attributes, Fields = fields };
    }
}
