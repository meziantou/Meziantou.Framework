namespace Meziantou.Framework.CodeDom;

public abstract class CodeObject
{
    public IDictionary<string, object?> Data { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public void SetData(string key, object? value)
    {
        Data[key] = value;
    }

    public CodeObject? Parent { get; internal set; }

    protected void SetParent<T>(ref T? field, T? value)
        where T : CodeObject
    {
        SetParent(this, ref field, value);
    }

    protected static void SetParent<T>(CodeObject parent, ref T? field, T? value) where T : CodeObject
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (field is not null)
        {
            field.Parent = null; // Detach previous value
        }

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

    public string ToCsharpString()
    {
        var generator = new CSharpCodeGenerator();
        return generator.Write(this);
    }
}
