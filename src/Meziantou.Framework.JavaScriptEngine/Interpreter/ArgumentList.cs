using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The argument list of a call with a spread argument (<c>f(...a)</c>, <c>new C(...a)</c>, <c>super(...a)</c>):
/// ArgumentListEvaluation, on the operand stack of the frame. It is never visible to the code: the call reads its values
/// without the array and the copy of an array literal.
/// </summary>
internal sealed class ArgumentList() : JsCell(JsValueKind.Undefined)
{
    private static readonly PropertyKey IteratorKey = new(WellKnownSymbols.Iterator);

    private JsValue[] _values = [];
    private int _count;

    /// <summary>Gets the arguments.</summary>
    public ReadOnlySpan<JsValue> Values => _values.AsSpan(0, _count);

    /// <summary>Gets the arguments in an array of their length (the storage when it has this length).</summary>
    public JsValue[] ToArray() => _count == _values.Length ? _values : Values.ToArray();

    public void Add(JsValue value)
    {
        if (_count == _values.Length)
        {
            Grow(_count + 1);
        }

        _values[_count++] = value;
    }

    /// <summary>Adds the values of an iterable (a spread argument).</summary>
    public void AddSpread(JsRealm realm, JsValue iterable)
    {
        // An array whose iteration is not observable (see IteratorOperations.GetIterator): the steps of the array
        // iterator, without the iterator (the length is read at each step, a hole is read in the prototypes)
        if (iterable.Cell is JsArray array && ReferenceEquals(array.PrototypeSlot, realm.Intrinsics.ArrayPrototype)
            && !array.TryFindOwnForCache(IteratorKey, out _, out _) && realm.Intrinsics.IsArrayIterationIntact())
        {
            if (array.Length > _values.Length - _count)
            {
                Grow(_count + (int)Math.Min(array.Length, 1024));
            }

            for (long index = 0; index < array.Length; index++)
            {
                Add(ArrayOperations.GetElement(array, index));
            }

            return;
        }

        var record = IteratorOperations.GetIterator(iterable);
        while (IteratorOperations.IteratorStepValue(record, out var value))
        {
            Add(value);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int minimumLength)
    {
        var length = Math.Max(minimumLength, Math.Max(4, _values.Length * 2));
        JsEngine.EnsureCurrentCanAllocate((long)length * Unsafe.SizeOf<JsValue>());
        Array.Resize(ref _values, length);
    }
}
