﻿using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.HumanReadable;

public abstract class HumanReadableConverter
{
    public virtual bool HandleNull { get; }

    public abstract bool CanConvert(Type type);

    public abstract void WriteValue(HumanReadableTextWriter writer, object? value, HumanReadableSerializerOptions options);

    [return: NotNullIfNotNull(nameof(converterAttribute))]
    internal static HumanReadableConverter? CreateFromAttribute(HumanReadableConverterAttribute? converterAttribute, Type typeToConvert)
    {
        HumanReadableConverter? converter = null;
        if (converterAttribute != null)
        {
            converterAttribute.EnsureTypeIsValid();

            converter = (HumanReadableConverter)Activator.CreateInstance(converterAttribute.ConverterType)!;
            if (!converter.HandleNull)
                converter = new NullConverterWrapper(converter);

            if (!converter.CanConvert(typeToConvert))
                throw new HumanReadableSerializerException($"The converter '{converter.GetType().FullName}' is not compatible with '{typeToConvert.FullName}'");
        }

        return converter;
    }
}
