using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The binding of an SVG DOM object to the attribute it reflects.</summary>
internal sealed class SvgAttributeBinding(Element element, string localName, bool readOnly, string? @namespace = null)
{
    private string? _lastValue;
    private bool _initialized;

    public Element Element { get; } = element;

    public string LocalName { get; } = localName;

    public bool IsReadOnly { get; } = readOnly;

    // The read-only objects are the animated values (animVal): the value of the SMIL animations in effect, or the attribute
    public string? Value => @namespace is not null ? Element.GetAttributeNS(@namespace, LocalName)
        : IsReadOnly ? Element.GetSampledAnimatedAttributeValue(LocalName)
        : Element.GetAttributeValue(LocalName);

    /// <summary>Returns whether the attribute changed since the last call.</summary>
    public bool HasChanged(out string? value)
    {
        value = Value;
        if (_initialized && string.Equals(value, _lastValue, StringComparison.Ordinal))
            return false;

        _initialized = true;
        _lastValue = value;
        return true;
    }

    public void Write(string value)
    {
        _initialized = true;
        _lastValue = value;
        if (@namespace is null)
        {
            Element.SetAttributeValue(LocalName, value);
        }
        else
        {
            Element.SetAttributeNS(@namespace, LocalName, value);
        }
    }
}

/// <summary>The list interfaces of SVG (https://svgwg.org/svg2-draft/types.html#ListInterfaces).</summary>
[IdlInterface("SVGNameList", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGNameList")]
public abstract class SvgListBase<T> : IReadOnlyList<T>, ISvgValueOwner
    where T : notnull
{
    private readonly SvgAttributeBinding? _binding;
    private readonly List<T> _items = [];

    private protected SvgListBase(SvgAttributeBinding? binding)
    {
        _binding = binding;
    }

    bool ISvgValueOwner.IsReadOnly => _binding?.IsReadOnly ?? false;

    /// <summary>Gets the number of items of the list.</summary>
    [IdlMember("length", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__length")]
    public uint Length => NumberOfItems;

    /// <summary>Gets the number of items of the list.</summary>
    [IdlMember("numberOfItems", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__numberOfItems")]
    public uint NumberOfItems
    {
        get
        {
            Sync();
            return (uint)_items.Count;
        }
    }

    /// <summary>Gets the number of items of the list.</summary>
    [NotInIdl("IReadOnlyList implementation")]
    public int Count => (int)NumberOfItems;

    /// <summary>Gets or sets the item at the given index; setting an item replaces the existing one.</summary>
    [IdlMember("setter", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__setter")]
    public T this[uint index]
    {
        get => GetItem(index);
        set => ReplaceItem(value, index);
    }

    /// <summary>Gets the item at the given index.</summary>
    [NotInIdl("IReadOnlyList implementation")]
    public T this[int index] => GetItem(checked((uint)index));

    /// <summary>Removes all the items of the list.</summary>
    [IdlMember("clear", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__clear")]
    public void Clear()
    {
        CheckWritable();
        DetachAll();
        _items.Clear();
        Commit();
    }

    /// <summary>Clears the list, then inserts the given item as its only item and returns it.</summary>
    [IdlMember("initialize", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__initialize")]
    public T Initialize(T newItem)
    {
        ArgumentNullException.ThrowIfNull(newItem);
        CheckWritable();
        newItem = PrepareNewItem(newItem);
        DetachAll();
        _items.Clear();
        _items.Add(newItem);
        Commit();
        return newItem;
    }

    /// <summary>
    /// Returns the item at the given index, or throws an <c>IndexSizeError</c> when the index is out of range.
    /// </summary>
    [IdlMember("getItem", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__getItem")]
    public T GetItem(uint index)
    {
        Sync();
        if (index >= _items.Count)
            throw DomException.IndexSize($"The index {index} is out of range");

        return _items[(int)index];
    }

    /// <summary>
    /// Inserts the item before the item at the given index (at the end if the index is too large) and returns it.
    /// </summary>
    [IdlMember("insertItemBefore", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__insertItemBefore")]
    public T InsertItemBefore(T newItem, uint index)
    {
        ArgumentNullException.ThrowIfNull(newItem);
        CheckWritable();
        newItem = PrepareNewItem(newItem);
        _items.Insert((int)Math.Min(index, (uint)_items.Count), newItem);
        Commit();
        return newItem;
    }

    /// <summary>Replaces the item at the given index with the given item and returns the new item.</summary>
    [IdlMember("replaceItem", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__replaceItem")]
    public T ReplaceItem(T newItem, uint index)
    {
        ArgumentNullException.ThrowIfNull(newItem);
        CheckWritable();
        if (index >= _items.Count)
            throw DomException.IndexSize($"The index {index} is out of range");

        newItem = PrepareNewItem(newItem);
        SetOwner(_items[(int)index], null);
        _items[(int)index] = newItem;
        Commit();
        return newItem;
    }

    /// <summary>Removes the item at the given index from the list and returns it.</summary>
    [IdlMember("removeItem", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__removeItem")]
    public T RemoveItem(uint index)
    {
        CheckWritable();
        if (index >= _items.Count)
            throw DomException.IndexSize($"The index {index} is out of range");

        var item = _items[(int)index];
        _items.RemoveAt((int)index);
        SetOwner(item, null);
        Commit();
        return item;
    }

    /// <summary>Appends the item to the list and returns it.</summary>
    [IdlMember("appendItem", "https://w3c.github.io/svgwg/svg2-draft/types.html#__svg__SVGNameList__appendItem")]
    public T AppendItem(T newItem)
    {
        ArgumentNullException.ThrowIfNull(newItem);
        CheckWritable();
        newItem = PrepareNewItem(newItem);
        _items.Add(newItem);
        Commit();
        return newItem;
    }

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<T> GetEnumerator()
    {
        Sync();
        return _items.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void CheckWritable()
    {
        if (_binding?.IsReadOnly == true)
            throw DomException.NoModificationAllowed("The list is read only");

        Sync();
    }

    // An item that belongs to another list or reflects an attribute is copied
    private T PrepareNewItem(T newItem)
    {
        if (GetOwner(newItem) is not null)
        {
            newItem = Copy(newItem);
        }

        SetOwner(newItem, this);
        return newItem;
    }

    private void DetachAll()
    {
        foreach (var item in _items)
        {
            SetOwner(item, null);
        }
    }

    /// <summary>Updates the items from the attribute when it changed.</summary>
    internal void Sync()
    {
        if (_binding is null || !_binding.HasChanged(out var value))
            return;

        // https://svgwg.org/svg2-draft/types.html#ListInterfaces: the existing items are updated in place, the items beyond
        // the new length are detached, and new items are appended
        var newItems = value is not null && TryParse(value, out var items) ? items : [];
        var common = Math.Min(_items.Count, newItems.Count);
        for (var i = 0; i < common; i++)
        {
            if (!TryAssign(_items[i], newItems[i]))
            {
                SetOwner(_items[i], null);
                _items[i] = newItems[i];
                SetOwner(newItems[i], this);
            }
        }

        for (var i = common; i < _items.Count; i++)
        {
            SetOwner(_items[i], null);
        }

        if (_items.Count > common)
        {
            _items.RemoveRange(common, _items.Count - common);
        }

        for (var i = common; i < newItems.Count; i++)
        {
            SetOwner(newItems[i], this);
            _items.Add(newItems[i]);
        }
    }

    /// <summary>Copies the value of the source item into the target item; returns false when items are immutable.</summary>
    private protected virtual bool TryAssign(T target, T source) => false;

    internal void Commit()
    {
        _binding?.Write(Serialize(_items));
    }

    void ISvgValueOwner.Sync() => Sync();

    void ISvgValueOwner.Commit() => Commit();

    /// <summary>Returns the items without synchronizing them with the attribute.</summary>
    internal List<T> RawItems => _items;

    private protected abstract bool TryParse(string value, out List<T> items);

    private protected abstract string Serialize(List<T> items);

    private protected abstract ISvgValueOwner? GetOwner(T item);

    private protected abstract void SetOwner(T item, ISvgValueOwner? owner);

    private protected abstract T Copy(T item);
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGNumberList</summary>
[IdlInterface("SVGNumberList", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGNumberList")]
public sealed class SvgNumberList : SvgListBase<SvgNumber>
{
    internal SvgNumberList(SvgAttributeBinding? binding)
        : base(binding)
    {
    }

    private protected override bool TryParse(string value, out List<SvgNumber> items)
    {
        items = [];
        if (!SvgParsing.TryParseNumberList(value, out var numbers))
            return false;

        items = numbers.ConvertAll(n => new SvgNumber(n));
        return true;
    }

    private protected override string Serialize(List<SvgNumber> items) => string.Join(' ', items.Select(i => i.Serialize()));

    private protected override ISvgValueOwner? GetOwner(SvgNumber item) => item.Owner;

    private protected override void SetOwner(SvgNumber item, ISvgValueOwner? owner) => item.Owner = owner;

    private protected override SvgNumber Copy(SvgNumber item) => item.Copy();

    private protected override bool TryAssign(SvgNumber target, SvgNumber source)
    {
        target.RawValue = source.RawValue;
        return true;
    }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGLengthList</summary>
[IdlInterface("SVGLengthList", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGLengthList")]
public sealed class SvgLengthList : SvgListBase<SvgLength>
{
    private readonly (Element Element, string Attribute)? _context;

    internal SvgLengthList(SvgAttributeBinding? binding)
        : base(binding)
    {
        _context = binding is null ? null : (binding.Element, binding.LocalName);
    }

    private protected override bool TryParse(string value, out List<SvgLength> items)
    {
        items = [];
        var position = 0;
        SvgParsing.SkipWhitespace(value, ref position);
        while (position < value.Length)
        {
            var start = position;
            while (position < value.Length && value[position] != ',' && !Infra.InfraAscii.IsAsciiWhitespace(value[position]))
            {
                position++;
            }

            if (!SvgLength.TryParse(value[start..position], out var number, out var unitType, out var unknownUnit))
                return false;

            var length = new SvgLength();
            length.SetRaw(number, unitType, unknownUnit);
            items.Add(length);
            if (SvgParsing.SkipCommaWhitespace(value, ref position) && position >= value.Length)
                return false;
        }

        return true;
    }

    private protected override string Serialize(List<SvgLength> items) => string.Join(' ', items.Select(i => i.Serialize()));

    private protected override ISvgValueOwner? GetOwner(SvgLength item) => item.Owner;

    private protected override void SetOwner(SvgLength item, ISvgValueOwner? owner)
    {
        // The relative units of the items of the list of an attribute are resolved for its element
        item.Owner = owner;
        item.Context = owner is null ? null : _context;
    }

    private protected override SvgLength Copy(SvgLength item) => item.Copy();

    private protected override bool TryAssign(SvgLength target, SvgLength source)
    {
        target.AssignFrom(source);
        return true;
    }
}

/// <summary>https://svgwg.org/svg2-draft/shapes.html#InterfaceSVGPointList</summary>
[IdlInterface("SVGPointList", "https://w3c.github.io/svgwg/svg2-draft/shapes.html#InterfaceSVGPointList")]
public sealed class SvgPointList : SvgListBase<DomPoint>
{
    internal SvgPointList(SvgAttributeBinding? binding)
        : base(binding)
    {
    }

    private protected override bool TryParse(string value, out List<DomPoint> items)
    {
        // https://svgwg.org/svg2-draft/shapes.html#DataTypePoints: an invalid value is an empty list, and an odd
        // number of coordinates ignores the last one
        items = [];
        if (!SvgParsing.TryParseNumberList(value, out var numbers))
        {
            // An x coordinate followed by a comma without y coordinate at the end of the list is an incomplete point too
            var trimmed = value.AsSpan().TrimEnd(" \t\n\f\r");
            if (trimmed.Length == 0 || trimmed[^1] != ',' || !SvgParsing.TryParseNumberList(trimmed[..^1].ToString(), out numbers) || numbers.Count % 2 == 0)
                return false;
        }

        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            items.Add(new SvgListPoint(numbers[i], numbers[i + 1]));
        }

        return true;
    }

    private protected override string Serialize(List<DomPoint> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(SvgParsing.FormatNumber((float)item.X)).Append(',').Append(SvgParsing.FormatNumber((float)item.Y));
        }

        return builder.ToString();
    }

    private protected override ISvgValueOwner? GetOwner(DomPoint item) => (item as SvgListPoint)?.Owner;

    private protected override void SetOwner(DomPoint item, ISvgValueOwner? owner)
    {
        if (item is SvgListPoint point)
        {
            point.Owner = owner;
        }
    }

    private protected override DomPoint Copy(DomPoint item) => new SvgListPoint(item.X, item.Y);

    private protected override bool TryAssign(DomPoint target, DomPoint source)
    {
        if (target is not SvgListPoint point)
            return false;

        point.SetRaw(source.X, source.Y);
        return true;
    }
}

/// <summary>A point of an SVGPointList: changes are written to the attribute.</summary>
internal sealed class SvgListPoint(double x, double y) : DomPoint(x, y)
{
    public ISvgValueOwner? Owner { get; set; }

    public void SetRaw(double x, double y)
    {
        XValue = x;
        YValue = y;
    }

    public override double X
    {
        get
        {
            Owner?.Sync();
            return base.X;
        }
        set
        {
            Owner.BeginChange();
            base.X = value;
            Owner?.Commit();
        }
    }

    public override double Y
    {
        get
        {
            Owner?.Sync();
            return base.Y;
        }
        set
        {
            Owner.BeginChange();
            base.Y = value;
            Owner?.Commit();
        }
    }
}

/// <summary>https://svgwg.org/svg2-draft/types.html#InterfaceSVGStringList</summary>
[IdlInterface("SVGStringList", "https://w3c.github.io/svgwg/svg2-draft/types.html#InterfaceSVGStringList")]
public sealed class SvgStringList : SvgListBase<string>
{
    private readonly bool _commaSeparated;

    internal SvgStringList(SvgAttributeBinding? binding, bool commaSeparated)
        : base(binding)
    {
        _commaSeparated = commaSeparated;
    }

    private protected override bool TryParse(string value, out List<string> items)
    {
        items = SvgParsing.SplitList(value, _commaSeparated);
        return true;
    }

    private protected override string Serialize(List<string> items) => string.Join(_commaSeparated ? ", " : " ", items);

    private protected override ISvgValueOwner? GetOwner(string item) => null;

    private protected override void SetOwner(string item, ISvgValueOwner? owner)
    {
    }

    private protected override string Copy(string item) => item;
}

/// <summary>https://svgwg.org/svg2-draft/coords.html#InterfaceSVGTransformList</summary>
[IdlInterface("SVGTransformList", "https://w3c.github.io/svgwg/svg2-draft/coords.html#InterfaceSVGTransformList")]
public sealed class SvgTransformList : SvgListBase<SvgTransform>
{
    internal SvgTransformList(SvgAttributeBinding? binding)
        : base(binding)
    {
    }

    /// <summary>
    /// Creates a transform of type <c>SVG_TRANSFORM_MATRIX</c> from the given matrix, without adding it to the list.
    /// </summary>
    [IdlMember("createSVGTransformFromMatrix", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransformList__createSVGTransformFromMatrix")]
    public SvgTransform CreateSvgTransformFromMatrix(DomMatrix2DInit? matrix = null)
    {
        var transform = new SvgTransform();
        transform.SetMatrix(matrix);
        return transform;
    }

    /// <summary>https://svgwg.org/svg2-draft/coords.html#__svg__SVGTransformList__consolidate</summary>
    [IdlMember("consolidate", "https://w3c.github.io/svgwg/svg2-draft/coords.html#__svg__SVGTransformList__consolidate")]
    public SvgTransform? Consolidate()
    {
        var count = NumberOfItems;
        if (count == 0)
            return null;

        if (count == 1)
            return GetItem(0);

        var matrix = new DomMatrix();
        foreach (var item in RawItems)
        {
            matrix.MultiplySelf(new DomMatrixInit { A = item.Matrix.A, B = item.Matrix.B, C = item.Matrix.C, D = item.Matrix.D, E = item.Matrix.E, F = item.Matrix.F });
        }

        var result = new SvgTransform();
        result.SetMatrixRaw(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
        return Initialize(result);
    }

    private protected override bool TryParse(string value, out List<SvgTransform> items) => SvgTransform.TryParseList(value, out items);

    private protected override string Serialize(List<SvgTransform> items) => SvgTransform.Serialize(items);

    private protected override ISvgValueOwner? GetOwner(SvgTransform item) => item.Owner;

    private protected override void SetOwner(SvgTransform item, ISvgValueOwner? owner) => item.Owner = owner;

    private protected override SvgTransform Copy(SvgTransform item) => item.Copy();

    private protected override bool TryAssign(SvgTransform target, SvgTransform source)
    {
        target.AssignFrom(source);
        return true;
    }
}
