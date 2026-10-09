namespace Meziantou.Framework.PublicApiGenerator;

internal readonly record struct PublicApiTypeFlags(
    bool IsStatic = false,
    bool IsAbstract = false,
    bool IsSealed = false,
    bool IsReadOnly = false,
    bool IsRefLike = false,
    bool IsClosed = false,
    bool IsUnion = false);
