using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>
/// The <c>(AudioContextRenderSizeCategory or unsigned long)</c> union of the render quantum size hint of a context
/// (https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-rendersizehint).
/// </summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-rendersizehint"/>
[IdlInterface("(AudioContextRenderSizeCategory or unsigned long)", "https://webaudio.github.io/web-audio-api/#dom-audiocontextoptions-rendersizehint")]
public readonly struct AudioContextRenderSizeHint : IUnionValue, IEquatable<AudioContextRenderSizeHint>
{
    // Null for the default value of the struct: the default category
    private readonly object? _value;

    private AudioContextRenderSizeHint(object value) => _value = value;

    /// <summary>Gets the value: an <see cref="AudioContextRenderSizeCategory"/> or a <see cref="uint"/>.</summary>
    [NotInIdl]
    public object Value => _value ?? AudioContextRenderSizeCategory.Default;

    /// <summary>Gets the category when the value is a category, otherwise null.</summary>
    [NotInIdl]
    public AudioContextRenderSizeCategory? Category => Value as AudioContextRenderSizeCategory?;

    /// <summary>Gets the number of sample frames when the value is a number, otherwise null.</summary>
    [NotInIdl]
    public uint? Size => Value as uint?;

    /// <summary>Creates a union value from a category.</summary>
    [NotInIdl]
    public static implicit operator AudioContextRenderSizeHint(AudioContextRenderSizeCategory value) => new(value);

    /// <summary>Creates a union value from a number of sample frames.</summary>
    [NotInIdl]
    public static implicit operator AudioContextRenderSizeHint(uint value) => new(value);

    /// <summary>Creates a union value from a category.</summary>
    [NotInIdl]
    public static AudioContextRenderSizeHint FromAudioContextRenderSizeCategory(AudioContextRenderSizeCategory value) => value;

    /// <summary>Creates a union value from a number of sample frames.</summary>
    [NotInIdl]
    public static AudioContextRenderSizeHint FromUInt32(uint value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(AudioContextRenderSizeHint other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is AudioContextRenderSizeHint other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Returns whether the two values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(AudioContextRenderSizeHint left, AudioContextRenderSizeHint right) => left.Equals(right);

    /// <summary>Returns whether the two values are different.</summary>
    [NotInIdl]
    public static bool operator !=(AudioContextRenderSizeHint left, AudioContextRenderSizeHint right) => !left.Equals(right);
}
