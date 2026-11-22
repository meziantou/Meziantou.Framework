namespace Meziantou.Framework.Csv.Serialization.Metadata;

/// <summary>The serialization metadata of <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The type described by the metadata.</typeparam>
/// <remarks>Get an instance from a generated <see cref="CsvSerializerContext"/>, or from <see cref="CsvSerializerOptions.GetTypeInfo{T}"/>.</remarks>
public sealed class CsvTypeInfo<T> : CsvTypeInfo
{
    private readonly CsvParameterInfoValues[] _parameters;
    private readonly CsvPropertyInfo<T>?[] _parameterProperties;

    internal CsvTypeInfo(CsvSerializerOptions options, CsvObjectInfoValues<T> values, CsvPropertyInfo<T>[] properties)
        : base(typeof(T), options, properties)
    {
        TypedProperties = properties;
        ObjectCreator = values.ObjectCreator;
        ParameterizedCreator = values.ObjectWithParameterizedConstructorCreator;

        var parameters = values.ConstructorParameters ?? [];
        _parameters = [.. parameters];
        _parameterProperties = new CsvPropertyInfo<T>[_parameters.Length];
        if (ParameterizedCreator is not null)
        {
            for (var i = 0; i < _parameters.Length; i++)
            {
                var parameter = _parameters[i];
                if (parameter.Position != i)
                    throw new InvalidOperationException($"The constructor parameters of '{typeof(T)}' must be ordered by position.");

                // A parameter without a member keeps its default value
                _parameterProperties[i] = Array.Find(properties, p => p.ParameterIndex == i);
                if (_parameterProperties[i] is null && !parameter.HasDefaultValue)
                    throw new InvalidOperationException($"The constructor parameter '{parameter.Name}' of '{typeof(T)}' has no member.");
            }
        }
        else if (ObjectCreator is null && !typeof(T).IsValueType)
        {
            throw new InvalidOperationException($"The metadata of '{typeof(T)}' has no way to create instances.");
        }

        // The columns are the members ordered by CsvPropertyOrder, or at their CsvColumnIndex
        var hasColumnIndex = Array.Exists(properties, p => p.ColumnIndex is not null);
        if (!hasColumnIndex)
        {
            Layout = properties;
        }
        else
        {
            var indices = new int[properties.Length];
            var maxIndex = -1;
            for (var i = 0; i < properties.Length; i++)
            {
                var index = properties[i].ColumnIndex ?? i;
                if (index < 0)
                    throw new InvalidOperationException($"The column index of '{typeof(T)}.{properties[i].MemberName}' is negative.");

                indices[i] = index;
                maxIndex = Math.Max(maxIndex, index);
            }

            Layout = new CsvPropertyInfo<T>?[maxIndex + 1];
            for (var i = 0; i < properties.Length; i++)
            {
                if (Layout[indices[i]] is { } other)
                    throw new InvalidOperationException($"The members '{other.MemberName}' and '{properties[i].MemberName}' of '{typeof(T)}' have the same column index {indices[i].ToString(CultureInfo.InvariantCulture)}.");

                Layout[indices[i]] = properties[i];
            }
        }

        HasOnDeserializing = typeof(ICsvOnDeserializing).IsAssignableFrom(typeof(T));
        HasOnDeserialized = typeof(ICsvOnDeserialized).IsAssignableFrom(typeof(T));
        HasOnSerializing = typeof(ICsvOnSerializing).IsAssignableFrom(typeof(T));
        HasOnSerialized = typeof(ICsvOnSerialized).IsAssignableFrom(typeof(T));
    }

    internal CsvPropertyInfo<T>[] TypedProperties { get; }

    // The member of each column, null for a column that no member uses
    internal CsvPropertyInfo<T>?[] Layout { get; }

    internal Func<T>? ObjectCreator { get; }

    internal Func<CsvConstructorArguments, T>? ParameterizedCreator { get; }

    // The arguments of the constructor, reused for every record of a file: each parameter has a typed slot, so that its value is not boxed
    internal CsvConstructorArguments CreateArguments()
    {
        var slots = new CsvArgumentSlot[_parameters.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var parameter = _parameters[i];
            slots[i] = _parameterProperties[i] is { } property
                ? property.CreateArgumentSlot(parameter.HasDefaultValue, parameter.DefaultValue)
                : new CsvBoxedArgumentSlot(parameter.DefaultValue);
        }

        return new CsvConstructorArguments(slots);
    }

    internal bool HasOnDeserializing { get; }

    internal bool HasOnDeserialized { get; }

    internal bool HasOnSerializing { get; }

    internal bool HasOnSerialized { get; }

    internal T CreateObject() => ObjectCreator is not null ? ObjectCreator() : default!;
}
