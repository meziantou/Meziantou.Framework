[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.FuzzEnums.FuzzColor))]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.FuzzEnums.FuzzPermission))]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.FuzzEnums.FuzzLarge))]

namespace Meziantou.Framework.FastEnumGenerator.FuzzEnums;

/// <summary>Each lambda is a call site the generator intercepts, so the calls must be written for each enum.</summary>
public static class FuzzEnumParsers
{
#pragma warning disable RS0030 // Banned API -- the overloads without ignoreCase are intercepted too
    public static EnumParsers<FuzzColor> Color { get; } = new()
    {
        Intercepted = [(value, ic) => Enum.Parse<FuzzColor>(value, ic), (value, ic) => Enum.Parse<FuzzColor>(value.AsSpan(), ic)],
        InterceptedCaseSensitive = [(value, _) => Enum.Parse<FuzzColor>(value), (value, _) => Enum.Parse<FuzzColor>(value.AsSpan())],
        InterceptedTry = [(string value, bool ic, out FuzzColor result) => Enum.TryParse(value, ic, out result), (string value, bool ic, out FuzzColor result) => Enum.TryParse(value.AsSpan(), ic, out result)],
        InterceptedTryCaseSensitive = [(string value, bool _, out FuzzColor result) => Enum.TryParse(value, out result), (string value, bool _, out FuzzColor result) => Enum.TryParse(value.AsSpan(), out result)],
        Generated = [(value, ic) => FuzzColor.Parse(value, ic), (value, ic) => FuzzColor.Parse(value.AsSpan(), ic)],
        GeneratedTry = [(string value, bool ic, out FuzzColor result) => FuzzColor.TryParse(value, ic, out result), (string value, bool ic, out FuzzColor result) => FuzzColor.TryParse(value.AsSpan(), ic, out result)],
        Format = value => value.ToString(),
    };

    public static EnumParsers<FuzzPermission> Permission { get; } = new()
    {
        Intercepted = [(value, ic) => Enum.Parse<FuzzPermission>(value, ic), (value, ic) => Enum.Parse<FuzzPermission>(value.AsSpan(), ic)],
        InterceptedCaseSensitive = [(value, _) => Enum.Parse<FuzzPermission>(value), (value, _) => Enum.Parse<FuzzPermission>(value.AsSpan())],
        InterceptedTry = [(string value, bool ic, out FuzzPermission result) => Enum.TryParse(value, ic, out result), (string value, bool ic, out FuzzPermission result) => Enum.TryParse(value.AsSpan(), ic, out result)],
        InterceptedTryCaseSensitive = [(string value, bool _, out FuzzPermission result) => Enum.TryParse(value, out result), (string value, bool _, out FuzzPermission result) => Enum.TryParse(value.AsSpan(), out result)],
        Generated = [(value, ic) => FuzzPermission.Parse(value, ic), (value, ic) => FuzzPermission.Parse(value.AsSpan(), ic)],
        GeneratedTry = [(string value, bool ic, out FuzzPermission result) => FuzzPermission.TryParse(value, ic, out result), (string value, bool ic, out FuzzPermission result) => FuzzPermission.TryParse(value.AsSpan(), ic, out result)],
        Format = value => value.ToString(),
    };

    public static EnumParsers<FuzzLarge> Large { get; } = new()
    {
        Intercepted = [(value, ic) => Enum.Parse<FuzzLarge>(value, ic), (value, ic) => Enum.Parse<FuzzLarge>(value.AsSpan(), ic)],
        InterceptedCaseSensitive = [(value, _) => Enum.Parse<FuzzLarge>(value), (value, _) => Enum.Parse<FuzzLarge>(value.AsSpan())],
        InterceptedTry = [(string value, bool ic, out FuzzLarge result) => Enum.TryParse(value, ic, out result), (string value, bool ic, out FuzzLarge result) => Enum.TryParse(value.AsSpan(), ic, out result)],
        InterceptedTryCaseSensitive = [(string value, bool _, out FuzzLarge result) => Enum.TryParse(value, out result), (string value, bool _, out FuzzLarge result) => Enum.TryParse(value.AsSpan(), out result)],
        Generated = [(value, ic) => FuzzLarge.Parse(value, ic), (value, ic) => FuzzLarge.Parse(value.AsSpan(), ic)],
        GeneratedTry = [(string value, bool ic, out FuzzLarge result) => FuzzLarge.TryParse(value, ic, out result), (string value, bool ic, out FuzzLarge result) => FuzzLarge.TryParse(value.AsSpan(), ic, out result)],
        Format = value => value.ToString(),
    };
#pragma warning restore RS0030
}
