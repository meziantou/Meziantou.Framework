namespace Meziantou.Framework.Json.Internals;

// All nested queries and equality walks use this same navigator, so they share the evaluation's budget.
internal sealed class EvaluationNavigator<TValue> : JsonPathNavigator<TValue>
{
    private readonly JsonPathNavigator<TValue> _inner;
    private readonly CancellationToken _cancellationToken;
    private readonly long? _maximumWork;
    private long _work;

    public EvaluationNavigator(JsonPathNavigator<TValue> inner, JsonPathEvaluationOptions? options)
    {
        _inner = inner;
        _cancellationToken = options?.CancellationToken ?? default;
        _maximumWork = options?.MaximumWork;
        MaximumResults = options?.MaximumResults;
        MaximumIntermediateResults = options?.MaximumIntermediateResults;
        if (_maximumWork < 0 || MaximumResults < 0 || MaximumIntermediateResults < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Evaluation limits must not be negative.");
        Check();
    }

    public int? MaximumResults { get; }

    public int? MaximumIntermediateResults { get; }

    public Dictionary<FunctionCallExpression, double> Counts { get; } = [];

    public override bool IsPure => _inner.IsPure;

    public void Check(long work = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_maximumWork is { } maximum)
        {
            if (work > maximum - _work)
                throw new JsonPathEvaluationException("The maximum evaluation work budget was exceeded.");

            _work += work;
        }
    }

    public override JsonPathNodeKind GetKind(TValue? value)
    {
        Check();
        return _inner.GetKind(value);
    }

    public override bool TryGetPropertyValue(TValue? value, string name, out TValue? result)
    {
        Check();
        return _inner.TryGetPropertyValue(value, name, out result);
    }

    public override IEnumerable<JsonPathProperty<TValue>> GetProperties(TValue? value)
    {
        Check();
        foreach (var property in _inner.GetProperties(value))
        {
            Check();
            yield return property;
        }
    }

    public override int GetArrayLength(TValue? value)
    {
        Check();
        return _inner.GetArrayLength(value);
    }

    public override bool TryGetElement(TValue? value, int index, out TValue? result)
    {
        Check();
        return _inner.TryGetElement(value, index, out result);
    }

    public override bool TryGetString(TValue? value, out string? result)
    {
        Check();
        return _inner.TryGetString(value, out result);
    }

    public override bool TryGetNumber(TValue? value, out double result)
    {
        Check();
        return _inner.TryGetNumber(value, out result);
    }

    public override bool TryGetBoolean(TValue? value, out bool result)
    {
        Check();
        return _inner.TryGetBoolean(value, out result);
    }

    internal override bool HasConstantTimeElementAccess => _inner.HasConstantTimeElementAccess;

    internal override void CopyElements(TValue? value, TValue?[] destination)
    {
        Check(_inner.GetArrayLength(value));
        _inner.CopyElements(value, destination);
        Check();
    }
}
