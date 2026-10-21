using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The scope returned by <see cref="JsEngine.Enter"/>: disposing it restores the previous engine of the thread.</summary>
public readonly struct JsEngineScope : IDisposable, IEquatable<JsEngineScope>
{
    private readonly JsEngine? _previous;

    internal JsEngineScope(JsEngine? previous) => _previous = previous;

    /// <summary>Restores the previous engine of the thread.</summary>
    public void Dispose() => JsEngine.RestoreCurrent(_previous);

    /// <inheritdoc/>
    public bool Equals(JsEngineScope other) => ReferenceEquals(_previous, other._previous);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsEngineScope other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _previous?.GetHashCode() ?? 0;

    /// <summary>Compares two scopes.</summary>
    public static bool operator ==(JsEngineScope left, JsEngineScope right) => left.Equals(right);

    /// <summary>Compares two scopes.</summary>
    public static bool operator !=(JsEngineScope left, JsEngineScope right) => !left.Equals(right);
}
