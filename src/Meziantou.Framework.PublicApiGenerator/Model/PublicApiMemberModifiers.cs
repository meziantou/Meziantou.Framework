namespace Meziantou.Framework.PublicApiGenerator;

internal readonly record struct PublicApiMemberModifiers(
    bool IsStatic = false,
    bool IsAbstract = false,
    bool IsVirtual = false,
    bool IsOverride = false,
    bool IsSealed = false,
    bool RequiresUnsafe = false,
    bool IsExplicitInterfaceImplementation = false);
