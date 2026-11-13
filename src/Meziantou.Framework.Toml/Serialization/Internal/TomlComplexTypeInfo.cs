using Meziantou.Framework.Toml.Serialization.Converters;

namespace Meziantou.Framework.Toml.Serialization.Internal;

// The metadata of Complex and Complex<T>. The metadata of the parts is resolved on first use, through the same resolver as
// the elements of a collection: the reflection pipeline or the source-generated context.
internal sealed class TomlComplexTypeInfo<TComplex, TPart> : TomlTypeInfo<TComplex>
    where TComplex : struct
    where TPart : struct
{
    private readonly Func<TomlTypeInfo?> _resolvePartTypeInfo;
    private readonly Func<TPart, TPart, TComplex> _create;
    private readonly Func<TComplex, TPart> _getReal;
    private readonly Func<TComplex, TPart> _getImaginary;
    private TomlTypeInfo? _partTypeInfo;

    public TomlComplexTypeInfo(TomlSerializerOptions options, Func<TomlTypeInfo?> resolvePartTypeInfo, Func<TPart, TPart, TComplex> create, Func<TComplex, TPart> getReal, Func<TComplex, TPart> getImaginary)
        : base(options)
    {
        _resolvePartTypeInfo = resolvePartTypeInfo;
        _create = create;
        _getReal = getReal;
        _getImaginary = getImaginary;
    }

    public override TComplex Read(TomlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var (real, imaginary) = TomlComplexConverter.Read<TPart>(reader, GetPartTypeInfo(), typeof(TComplex));
        return _create(real, imaginary);
    }

    public override void Write(TomlWriter writer, TComplex value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        TomlComplexConverter.Write(writer, _getReal(value), _getImaginary(value), GetPartTypeInfo());
    }

    private TomlTypeInfo GetPartTypeInfo()
    {
        return _partTypeInfo ??= _resolvePartTypeInfo()
            ?? throw TomlException.CreateConfigurationError($"No TOML metadata is available for type '{typeof(TPart).FullName}'.");
    }
}
