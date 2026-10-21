namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The [[PrivateElements]] of an object (https://tc39.es/ecma262/#sec-privateelement-specification-type).</summary>
internal sealed class PrivateElementTable
{
    private readonly List<Entry> _entries = [];

    private struct Entry
    {
        public PrivateName Name;
        public PrivateElementKind Kind;
        public JsValue Value;
        public JsValue Getter;
        public JsValue Setter;
    }

    /// <summary>PrivateElementFind: the index of the element, or -1.</summary>
    public int Find(PrivateName name)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Name, name))
                return i;
        }

        return -1;
    }

    public bool Contains(PrivateName name) => Find(name) >= 0;

    public void AddField(PrivateName name, JsValue value) => _entries.Add(new Entry { Name = name, Kind = PrivateElementKind.Field, Value = value });

    public void AddMethod(PrivateName name, JsValue method) => _entries.Add(new Entry { Name = name, Kind = PrivateElementKind.Method, Value = method });

    public void AddAccessor(PrivateName name, JsValue getter, JsValue setter) => _entries.Add(new Entry { Name = name, Kind = PrivateElementKind.Accessor, Getter = getter, Setter = setter });

    public PrivateElementKind GetKind(int index) => _entries[index].Kind;

    public JsValue GetValue(int index) => _entries[index].Value;

    public void SetValue(int index, JsValue value) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_entries)[index].Value = value;

    /// <summary>Completes an accessor with a getter or a setter (a getter and a setter of the same name are one element).</summary>
    public void UpdateAccessor(int index, JsValue getter, JsValue setter)
    {
        ref var entry = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_entries)[index];
        if (!getter.IsUndefined)
        {
            entry.Getter = getter;
        }

        if (!setter.IsUndefined)
        {
            entry.Setter = setter;
        }
    }

    public JsValue GetGetter(int index) => _entries[index].Getter;

    public JsValue GetSetter(int index) => _entries[index].Setter;
}
