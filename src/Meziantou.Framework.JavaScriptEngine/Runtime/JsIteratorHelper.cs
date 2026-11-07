namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The [[GeneratorState]] of an iterator helper.</summary>
internal enum IteratorHelperState : byte
{
    SuspendedStart,
    SuspendedYield,
    Executing,
    Completed,
}

/// <summary>
/// An Iterator Helper object (https://tc39.es/ecma262/#sec-iterator-helper-objects): the generator-like object created by
/// CreateIteratorFromClosure for the iterator helpers (map, filter, take, drop, flatMap) and Iterator.concat. The
/// closure is a state machine: <see cref="MoveNext"/> runs it until its next Yield, <see cref="ResumeWithReturn"/>
/// resumes it, suspended at a Yield, with a return completion.
/// </summary>
internal abstract class JsIteratorHelper(JsObject prototype) : JsObject(prototype, ObjectClass.Iterator)
{
    public IteratorHelperState State { get; set; }

    /// <summary>IteratorCloseAll([[UnderlyingIterators]], NormalCompletion): return() called before the first next().</summary>
    public abstract void CloseUnderlyingIterators();

    /// <summary>Resumes the closure with a normal completion: true when it yields a value, false when it returns.</summary>
    public abstract bool MoveNext(out JsValue value);

    /// <summary>Resumes the closure suspended at a Yield with a return completion.</summary>
    public abstract void ResumeWithReturn();
}

/// <summary>An iterator helper whose [[UnderlyingIterators]] is \u00AB iterated \u00BB.</summary>
internal abstract class JsUnderlyingIteratorHelper(JsObject prototype, IteratorRecord iterated) : JsIteratorHelper(prototype)
{
    protected IteratorRecord Iterated { get; } = iterated;

    public override void CloseUnderlyingIterators() => IteratorOperations.IteratorClose(Iterated);

    // IfAbruptCloseIterator(completion, iterated) with the return completion of the Yield
    public override void ResumeWithReturn() => IteratorOperations.IteratorClose(Iterated);

    /// <summary>Calls the function of the helper; an exception closes the underlying iterator (IfAbruptCloseIterator).</summary>
    protected JsValue CallClosing(JsObject function, JsValue value, double counter)
    {
        JsException exception;
        try
        {
            return function.Call(JsValue.Undefined, [value, counter]);
        }
        catch (JsException ex)
        {
            exception = ex;
        }

        return IteratorOperations.IteratorCloseAndRethrow<JsValue>(Iterated, exception);
    }
}

/// <summary>Iterator.prototype.map</summary>
internal sealed class JsMapIteratorHelper(JsObject prototype, IteratorRecord iterated, JsObject mapper) : JsUnderlyingIteratorHelper(prototype, iterated)
{
    private double _counter;

    public override bool MoveNext(out JsValue value)
    {
        if (!IteratorOperations.IteratorStepValue(Iterated, out var item))
        {
            value = default;
            return false;
        }

        value = CallClosing(mapper, item, _counter);
        _counter++;
        return true;
    }
}

/// <summary>Iterator.prototype.filter</summary>
internal sealed class JsFilterIteratorHelper(JsObject prototype, IteratorRecord iterated, JsObject predicate) : JsUnderlyingIteratorHelper(prototype, iterated)
{
    private double _counter;

    public override bool MoveNext(out JsValue value)
    {
        while (IteratorOperations.IteratorStepValue(Iterated, out var item))
        {
            var selected = CallClosing(predicate, item, _counter);
            _counter++;
            if (JsOperations.ToBoolean(selected))
            {
                value = item;
                return true;
            }
        }

        value = default;
        return false;
    }
}

/// <summary>Iterator.prototype.take</summary>
internal sealed class JsTakeIteratorHelper(JsObject prototype, IteratorRecord iterated, double limit) : JsUnderlyingIteratorHelper(prototype, iterated)
{
    private double _remaining = limit;

    public override bool MoveNext(out JsValue value)
    {
        value = default;
        if (_remaining == 0)
        {
            // Return ? IteratorClose(iterated, ReturnCompletion(undefined))
            IteratorOperations.IteratorClose(Iterated);
            return false;
        }

        if (!double.IsPositiveInfinity(_remaining))
        {
            _remaining--;
        }

        return IteratorOperations.IteratorStepValue(Iterated, out value);
    }
}

/// <summary>Iterator.prototype.drop</summary>
internal sealed class JsDropIteratorHelper(JsObject prototype, IteratorRecord iterated, double limit) : JsUnderlyingIteratorHelper(prototype, iterated)
{
    private double _remaining = limit;

    public override bool MoveNext(out JsValue value)
    {
        value = default;
        while (_remaining > 0)
        {
            if (!double.IsPositiveInfinity(_remaining))
            {
                _remaining--;
            }

            if (!IteratorBuiltins.IteratorStep(Iterated))
                return false;
        }

        return IteratorOperations.IteratorStepValue(Iterated, out value);
    }
}

/// <summary>Iterator.prototype.flatMap</summary>
internal sealed class JsFlatMapIteratorHelper(JsObject prototype, IteratorRecord iterated, JsObject mapper) : JsUnderlyingIteratorHelper(prototype, iterated)
{
    private double _counter;
    private IteratorRecord? _inner;

    public override bool MoveNext(out JsValue value)
    {
        while (true)
        {
            if (_inner is { } inner)
            {
                var hasValue = false;
                value = default;
                JsException? exception = null;
                try
                {
                    hasValue = IteratorOperations.IteratorStepValue(inner, out value);
                }
                catch (JsException ex)
                {
                    exception = ex;
                }

                if (exception is not null)
                {
                    IteratorOperations.IteratorCloseAndRethrow(Iterated, exception);
                }

                if (hasValue)
                    return true;

                _inner = null;
            }

            if (!IteratorOperations.IteratorStepValue(Iterated, out var item))
            {
                value = default;
                return false;
            }

            var mapped = CallClosing(mapper, item, _counter);
            JsException? flattenException = null;
            try
            {
                _inner = IteratorBuiltins.GetIteratorFlattenable(mapped, iterateStrings: false);
            }
            catch (JsException ex)
            {
                flattenException = ex;
            }

            if (flattenException is not null)
            {
                IteratorOperations.IteratorCloseAndRethrow(Iterated, flattenException);
            }

            _counter++;
        }
    }

    public override void ResumeWithReturn()
    {
        if (_inner is { } inner)
        {
            JsException? exception = null;
            try
            {
                IteratorOperations.IteratorClose(inner);
            }
            catch (JsException ex)
            {
                exception = ex;
            }

            if (exception is not null)
            {
                IteratorOperations.IteratorCloseAndRethrow(Iterated, exception);
            }
        }

        IteratorOperations.IteratorClose(Iterated);
    }
}

/// <summary>Iterator.concat: [[UnderlyingIterators]] is empty, the iterators are opened one after the other.</summary>
internal sealed class JsConcatIteratorHelper(JsObject prototype, (JsObject OpenMethod, JsObject Iterable)[] iterables) : JsIteratorHelper(prototype)
{
    private int _index;
    private IteratorRecord? _current;

    public override void CloseUnderlyingIterators()
    {
    }

    public override bool MoveNext(out JsValue value)
    {
        while (true)
        {
            if (_current is null)
            {
                if (_index >= iterables.Length)
                {
                    value = default;
                    return false;
                }

                var (openMethod, iterable) = iterables[_index++];
                var iterator = openMethod.Call(iterable, []);
                if (iterator.Cell is not JsObject iteratorObject)
                {
                    ThrowHelper.ThrowTypeError("Result of the Symbol.iterator method is not an object");
                    value = default;
                    return false;
                }

                _current = IteratorOperations.GetIteratorDirect(iteratorObject);
            }

            if (IteratorOperations.IteratorStepValue(_current, out value))
                return true;

            _current = null;
        }
    }

    public override void ResumeWithReturn()
    {
        if (_current is { } current)
        {
            IteratorOperations.IteratorClose(current);
        }
    }
}

/// <summary>An object of %WrapForValidIteratorPrototype% (Iterator.from): [[Iterated]].</summary>
internal sealed class JsWrapForValidIterator(JsObject prototype, IteratorRecord iterated) : JsObject(prototype, ObjectClass.Iterator)
{
    public IteratorRecord Iterated { get; } = iterated;
}
