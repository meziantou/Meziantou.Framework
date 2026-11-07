namespace Meziantou.Framework.FastEnumGenerator;

/// <summary>The <see cref="System.Enum"/> calls the generator can replace with an interceptor.</summary>
internal enum FastEnumInterceptKind
{
    None,
    ToString,
    HasFlag,
    IsDefined,
    GetName,
    GetNames,
    GetValues,
}
