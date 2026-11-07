using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The scope returned by <see cref="JsEngine.EnterActiveScriptOrModule"/>: disposing it pops the execution context.</summary>
public readonly struct JsScriptContextScope : IDisposable, IEquatable<JsScriptContextScope>
{
    private readonly JsEngine? _engine;
    private readonly int _count;

    internal JsScriptContextScope(JsEngine engine, int count)
    {
        _engine = engine;
        _count = count;
    }

    /// <summary>Pops the execution context.</summary>
    public void Dispose() => _engine?.ExitActiveScriptOrModule(_count);

    /// <inheritdoc/>
    public bool Equals(JsScriptContextScope other) => ReferenceEquals(_engine, other._engine) && _count == other._count;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsScriptContextScope other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_engine, _count);

    /// <summary>Compares two scopes.</summary>
    public static bool operator ==(JsScriptContextScope left, JsScriptContextScope right) => left.Equals(right);

    /// <summary>Compares two scopes.</summary>
    public static bool operator !=(JsScriptContextScope left, JsScriptContextScope right) => !left.Equals(right);
}
