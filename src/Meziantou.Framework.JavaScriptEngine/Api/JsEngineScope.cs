using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The scope returned by <see cref="JsEngine.Enter"/>: disposing it restores the previous engine of the thread.</summary>
public readonly struct JsEngineScope : IDisposable, IEquatable<JsEngineScope>
{
    private readonly JsEngine? _previous;

    // The engine whose ownership the scope took (the outermost scope of the thread), released by Dispose
    private readonly JsEngine? _owner;

    internal JsEngineScope(JsEngine? previous, JsEngine? owner)
    {
        _previous = previous;
        _owner = owner;
    }

    /// <summary>Restores the previous engine of the thread.</summary>
    public void Dispose()
    {
        JsEngine.RestoreCurrent(_previous);
        _owner?.ReleaseOwnership();
    }

    /// <inheritdoc/>
    public bool Equals(JsEngineScope other) => ReferenceEquals(_previous, other._previous) && ReferenceEquals(_owner, other._owner);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsEngineScope other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_previous, _owner);

    /// <summary>Compares two scopes.</summary>
    public static bool operator ==(JsEngineScope left, JsEngineScope right) => left.Equals(right);

    /// <summary>Compares two scopes.</summary>
    public static bool operator !=(JsEngineScope left, JsEngineScope right) => !left.Equals(right);
}
