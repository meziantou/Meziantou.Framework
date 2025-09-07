namespace Meziantou.AspNetCore.ServiceDefaults.AutoRegister;

internal enum InterceptionMethodKind
{
    Unknown,
    CreateBuilder,
    CreateBuilder_StringArray,
    CreateBuilderSlim,
    CreateBuilderSlim_StringArray,
    Build,
}
