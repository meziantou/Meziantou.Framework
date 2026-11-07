using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Iterator.prototype.chunks (proposal-iterator-chunking)</summary>
internal sealed class JsChunksIteratorHelper(JsObject prototype, IteratorRecord iterated, long chunkSize) : JsUnderlyingIteratorHelper(prototype, iterated)
{
    private List<JsValue> _buffer = [];

    // The last, partial, chunk was yielded: the underlying iterator is done
    private bool _finished;

    public override bool MoveNext(out JsValue value)
    {
        value = default;
        if (_finished)
            return false;

        while (true)
        {
            if (!IteratorOperations.IteratorStepValue(Iterated, out var item))
            {
                if (_buffer.Count == 0)
                    return false;

                _finished = true;
                value = TakeBuffer();
                return true;
            }

            _buffer.Add(item);
            if (_buffer.Count == chunkSize)
            {
                value = TakeBuffer();
                return true;
            }
        }
    }

    public override void ResumeWithReturn()
    {
        // The completion of the Yield of the last chunk is ignored
        if (!_finished)
        {
            IteratorOperations.IteratorClose(Iterated);
        }
    }

    private JsArray TakeBuffer()
    {
        var array = JsArray.Create(JsEngine.Current!.CurrentRealm!, CollectionsMarshal.AsSpan(_buffer));
        _buffer = [];
        return array;
    }
}

/// <summary>Iterator.prototype.windows (proposal-iterator-chunking)</summary>
internal sealed class JsWindowsIteratorHelper(JsObject prototype, IteratorRecord iterated, long windowSize, bool allowPartial) : JsUnderlyingIteratorHelper(prototype, iterated)
{
    private readonly List<JsValue> _buffer = [];

    // The partial window was yielded: the underlying iterator is done
    private bool _finished;

    public override bool MoveNext(out JsValue value)
    {
        value = default;
        if (_finished)
            return false;

        while (true)
        {
            if (!IteratorOperations.IteratorStepValue(Iterated, out var item))
            {
                if (allowPartial && _buffer.Count > 0 && _buffer.Count < windowSize)
                {
                    _finished = true;
                    value = CreateWindow();
                    return true;
                }

                return false;
            }

            if (_buffer.Count == windowSize)
            {
                _buffer.RemoveAt(0);
            }

            _buffer.Add(item);
            if (_buffer.Count == windowSize)
            {
                value = CreateWindow();
                return true;
            }
        }
    }

    public override void ResumeWithReturn()
    {
        // The completion of the Yield of the partial window is ignored
        if (!_finished)
        {
            IteratorOperations.IteratorClose(Iterated);
        }
    }

    private JsArray CreateWindow() => JsArray.Create(JsEngine.Current!.CurrentRealm!, CollectionsMarshal.AsSpan(_buffer));
}

/// <summary>The mode of Iterator.zip and Iterator.zipKeyed.</summary>
internal enum ZipMode
{
    Shortest,
    Longest,
    Strict,
}

/// <summary>
/// IteratorZip(iters, mode, padding, finishResults) (proposal-joint-iteration): the results are arrays, or
/// null-prototype objects whose keys are the keys of Iterator.zipKeyed.
/// </summary>
internal sealed class JsZipIteratorHelper : JsIteratorHelper
{
    private const string DifferentLengthsMessage = "The iterators of Iterator.zip in the strict mode have different lengths";

    private readonly IteratorRecord?[] _iters;
    private readonly List<IteratorRecord> _openIters;
    private readonly ZipMode _mode;
    private readonly JsValue[] _padding;
    private readonly PropertyKey[]? _keys;

    public JsZipIteratorHelper(JsObject prototype, List<IteratorRecord> iters, ZipMode mode, JsValue[] padding, PropertyKey[]? keys)
        : base(prototype)
    {
        _iters = [.. iters];
        _openIters = [.. iters];
        _mode = mode;
        _padding = padding;
        _keys = keys;
    }

    public override void CloseUnderlyingIterators() => IteratorBuiltins.IteratorCloseAll(_openIters);

    public override void ResumeWithReturn() => IteratorBuiltins.IteratorCloseAll(_openIters);

    public override bool MoveNext(out JsValue value)
    {
        value = default;
        var iterCount = _iters.Length;
        if (iterCount == 0)
            return false;

        var results = new JsValue[iterCount];
        for (var i = 0; i < iterCount; i++)
        {
            JsValue result = default;
            if (_iters[i] is not { } iter)
            {
                result = _padding[i];
            }
            else
            {
                var hasValue = false;
                JsException? exception = null;
                try
                {
                    hasValue = IteratorOperations.IteratorStepValue(iter, out result);
                }
                catch (JsException ex)
                {
                    exception = ex;
                }

                if (exception is not null)
                {
                    _openIters.Remove(iter);
                    IteratorBuiltins.IteratorCloseAllAndRethrow(_openIters, exception);
                }

                if (!hasValue)
                {
                    _openIters.Remove(iter);
                    switch (_mode)
                    {
                        case ZipMode.Shortest:
                            IteratorBuiltins.IteratorCloseAll(_openIters);
                            return false;

                        case ZipMode.Strict:
                            if (i != 0)
                            {
                                IteratorBuiltins.IteratorCloseAllAndThrowTypeError(_openIters, DifferentLengthsMessage);
                            }

                            for (var k = 1; k < iterCount; k++)
                            {
                                var other = _iters[k]!;
                                var open = false;
                                try
                                {
                                    open = IteratorBuiltins.IteratorStep(other);
                                }
                                catch (JsException ex)
                                {
                                    exception = ex;
                                }

                                if (exception is not null)
                                {
                                    _openIters.Remove(other);
                                    IteratorBuiltins.IteratorCloseAllAndRethrow(_openIters, exception);
                                }

                                if (open)
                                {
                                    IteratorBuiltins.IteratorCloseAllAndThrowTypeError(_openIters, DifferentLengthsMessage);
                                }

                                _openIters.Remove(other);
                            }

                            return false;

                        default:
                            if (_openIters.Count == 0)
                                return false;

                            _iters[i] = null;
                            result = _padding[i];
                            break;
                    }
                }
            }

            results[i] = result;
        }

        var realm = JsEngine.Current!.CurrentRealm!;
        if (_keys is null)
        {
            value = JsArray.Create(realm, results);
        }
        else
        {
            var obj = new JsObject(prototype: null);
            for (var i = 0; i < _keys.Length; i++)
            {
                obj.CreateDataPropertyOrThrow(_keys[i], results[i]);
            }

            value = obj;
        }

        return true;
    }
}
