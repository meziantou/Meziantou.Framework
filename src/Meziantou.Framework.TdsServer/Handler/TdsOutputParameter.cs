namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents the value of an output parameter sent back to the client.</summary>
public sealed class TdsOutputParameter
{
    /// <summary>Initializes a new instance of the <see cref="TdsOutputParameter"/> class.</summary>
    /// <param name="name">The parameter name, with or without the leading <c>@</c>.</param>
    /// <param name="type">The type of the value.</param>
    /// <param name="value">The value, or <see langword="null"/> for SQL NULL.</param>
    public TdsOutputParameter(string name, TdsColumnType type, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        Type = type;
        Value = value;
    }

    /// <summary>Gets the parameter name.</summary>
    public string Name { get; }

    /// <summary>Gets the type of the value.</summary>
    public TdsColumnType Type { get; }

    /// <summary>Gets the value. <see langword="null"/> and <see cref="DBNull.Value"/> are sent as SQL NULL.</summary>
    public object? Value { get; }
}
