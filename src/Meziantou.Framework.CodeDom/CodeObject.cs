namespace Meziantou.Framework.CodeDom;

/// <summary>Base class for all code objects in the CodeDOM tree.</summary>
/// <example>
/// <code>
/// var unit = new CompilationUnit();
/// var ns = unit.AddNamespace("MyNamespace");
/// var cls = ns.AddType(new ClassDeclaration("MyClass"));
/// </code>
/// </example>
public abstract class CodeObject
{
    /// <summary>Gets a dictionary for storing custom data associated with this code object.</summary>
    public IDictionary<string, object?> Data { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Sets custom data on this code object.</summary>
    /// <param name="key">The data key.</param>
    /// <param name="value">The data value.</param>
    public void SetData(string key, object? value)
    {
        Data[key] = value;
    }

    /// <summary>Gets or sets the parent code object in the CodeDOM tree.</summary>
    public CodeObject? Parent { get; internal set; }

    protected void SetParent<T>(ref T? field, T? value)
        where T : CodeObject
    {
        SetParent(this, ref field, value);
    }

    protected static void SetParent<T>(CodeObject parent, ref T? field, T? value) where T : CodeObject
    {
        ArgumentNullException.ThrowIfNull(parent);

        field?.Parent = null; // Detach previous value
        if (value is not null)
        {
            if (value.Parent is not null && value.Parent != parent)
                throw new ArgumentException("Object already has a parent.", nameof(value));

            value.Parent = parent;
        }

        field = value;
    }

    protected T SetParent<T>(T? value) where T : CodeObject
    {
        return SetParent(this, value);
    }

    protected static T SetParent<T>(CodeObject parent, T? value) where T : CodeObject
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (value?.Parent != parent)
            throw new ArgumentException("Object already has a parent.", nameof(value));

        value.Parent = parent;
        return value;
    }

    public override string ToString()
    {
        return ToCsharpString();
    }

    /// <summary>Converts this code object to C# code.</summary>
    /// <returns>The generated C# code.</returns>
    public string ToCsharpString()
    {
        var generator = new CSharpCodeGenerator();
        return generator.Write(this);
    }
}
